using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ImageChain.Core.Abstractions;
using ImageChain.Core.Configuration;
using ImageChain.Core.Models;
using Microsoft.Extensions.Logging;

namespace ImageChain.Core.Providers;

#pragma warning disable MEAI001

public sealed class CustomHttpImageProvider : IImageProvider
{
    private readonly HttpClient _httpClient;
    private readonly VendorConfig _vendor;
    private readonly ModelConfig _model;
    private readonly ILogger<CustomHttpImageProvider> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public CustomHttpImageProvider(
        HttpClient httpClient,
        VendorConfig vendor,
        ModelConfig model,
        ILogger<CustomHttpImageProvider> logger)
    {
        _httpClient = httpClient;
        _vendor = vendor;
        _model = model;
        _logger = logger;
    }

    public string VendorName => _vendor.Name;
    public string ModelId => _model.Id;
    public string DisplayName => _model.DisplayName ?? _model.Id;

    public bool SupportsTaskType(TaskType taskType)
    {
        return _model.Enabled && _model.Capabilities.ContainsKey(taskType.ToString());
    }

    public int GetPriority(TaskType taskType)
    {
        if (_model.Capabilities.TryGetValue(taskType.ToString(), out var cap))
            return cap.Priority;
        return 999;
    }

    public int GetRetryCount(TaskType taskType) => _model.RetryCount;

    public async Task<ImageGenerationResult> GenerateAsync(BaseTaskRequest request, CancellationToken ct = default)
    {
        try
        {
            var parameters = BuildParameters(request);
            var requestBody = BuildRequestBody(parameters);
            var endpoint = BuildEndpoint();

            _logger.LogDebug("[{Vendor}/{Model}] Calling {Endpoint}", _vendor.Name, _model.Id, endpoint);

            var httpMethod = _vendor.BaseEndpoint.Contains("tencent") || _vendor.BaseEndpoint.Contains("hunyuan")
                ? HttpMethod.Post : HttpMethod.Post;

            var httpRequest = new HttpRequestMessage(httpMethod, endpoint)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };

            ApplyAuth(httpRequest);

            var response = await _httpClient.SendAsync(httpRequest, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[{Vendor}/{Model}] HTTP {Status}: {Body}",
                    _vendor.Name, _model.Id, (int)response.StatusCode, responseBody[..Math.Min(200, responseBody.Length)]);
                return ImageGenerationResult.Failure(_vendor.Name, _model.Id,
                    $"HTTP {(int)response.StatusCode}: {responseBody}");
            }

            var imageBytes = ExtractImage(responseBody);
            if (imageBytes is null)
            {
                return ImageGenerationResult.Failure(_vendor.Name, _model.Id, "Failed to extract image from response");
            }

            var mediaType = request.MediaType ?? "image/png";
            return ImageGenerationResult.Success(imageBytes, _vendor.Name, _model.Id, mediaType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Vendor}/{Model}] Request failed", _vendor.Name, _model.Id);
            return ImageGenerationResult.Failure(_vendor.Name, _model.Id, ex.Message);
        }
    }

    private Dictionary<string, object?> BuildParameters(BaseTaskRequest request)
    {
        var parameters = new Dictionary<string, object?>();

        if (_model.DefaultParameters is not null)
        {
            foreach (var kv in _model.DefaultParameters)
                parameters[kv.Key] = kv.Value;
        }

        parameters["modelId"] = _model.Id;
        parameters["prompt"] = request switch
        {
            TextToImageRequest t2i => t2i.Prompt,
            ImageToImageRequest i2i => i2i.Prompt,
            _ => ""
        };

        if (request is TextToImageRequest textReq)
        {
            parameters["width"] = textReq.Width;
            parameters["height"] = textReq.Height;
            parameters["size"] = $"{textReq.Width}*{textReq.Height}";
            parameters["count"] = textReq.Count;
        }

        if (request is ImageToImageRequest imgReq)
        {
            parameters["image"] = TemplateEngine.ToBase64DataUri(imgReq.SourceImage, imgReq.SourceMediaType);
            if (imgReq.Strength.HasValue)
                parameters["strength"] = imgReq.Strength.Value;
        }

        if (request.ProviderParameters is not null)
        {
            foreach (var kv in request.ProviderParameters)
                parameters[kv.Key] = kv.Value;
        }

        return parameters;
    }

    private string BuildRequestBody(IDictionary<string, object?> parameters)
    {
        if (_model.RequestTemplate is not null)
        {
            return TemplateEngine.Resolve(_model.RequestTemplate, parameters);
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = _model.Id,
            ["prompt"] = parameters["prompt"]
        };

        if (parameters.TryGetValue("width", out var w) && parameters.TryGetValue("height", out var h))
            body["size"] = $"{w}x{h}";

        foreach (var kv in parameters)
        {
            if (kv.Key is "prompt" or "modelId" or "width" or "height" or "size" or "count")
                continue;
            body[kv.Key] = kv.Value;
        }

        return JsonSerializer.Serialize(body, JsonOptions);
    }

    private string BuildEndpoint()
    {
        var baseUri = _vendor.BaseEndpoint.TrimEnd('/');
        if (baseUri.Contains("/v1/models") || baseUri.Contains("/api/v1/models"))
            return $"{baseUri.Replace("/v1/models", "").Replace("/api/v1/models", "")}/v1/images/generations";
        return $"{baseUri}/v1/images/generations";
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (_vendor.AuthType.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            var headerValue = $"{_vendor.ApiKeyPrefix}{_vendor.ApiKey}";
            request.Headers.TryAddWithoutValidation(_vendor.ApiKeyHeader, headerValue);
        }
    }

    private byte[]? ExtractImage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);

            if (!string.IsNullOrEmpty(_model.ResponseImagePath))
            {
                return ExtractByJsonPath(doc, _model.ResponseImagePath);
            }

            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                return ExtractFromResultObject(doc.RootElement[0]);
            }

            if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            {
                return ExtractFromResultObject(data[0]);
            }

            if (doc.RootElement.TryGetProperty("result_image", out var resultImage))
            {
                return Convert.FromBase64String(resultImage.GetString()!);
            }

            if (doc.RootElement.TryGetProperty("ResultImage", out var resultImage2))
            {
                return Convert.FromBase64String(resultImage2.GetString()!);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? ExtractFromResultObject(JsonElement resultObject)
    {
        if (resultObject.TryGetProperty("b64_json", out var b64) &&
            !string.IsNullOrWhiteSpace(b64.GetString()))
        {
            return Convert.FromBase64String(b64.GetString()!);
        }

        if (resultObject.TryGetProperty("url", out var url) &&
            !string.IsNullOrWhiteSpace(url.GetString()))
        {
            return FetchImageFromUrl(url.GetString()!).GetAwaiter().GetResult();
        }

        return null;
    }

    private static byte[]? ExtractByJsonPath(JsonDocument doc, string jsonPath)
    {
        var parts = jsonPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        JsonElement current = doc.RootElement;

        foreach (var part in parts)
        {
            if (part.StartsWith('[') && part.EndsWith(']'))
            {
                var index = int.Parse(part[1..^1]);
                if (current.ValueKind == JsonValueKind.Array && index < current.GetArrayLength())
                {
                    current = current[index];
                }
                else return null;
            }
            else if (current.TryGetProperty(part, out var next))
            {
                current = next;
            }
            else return null;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => Convert.FromBase64String(current.GetString()!),
            JsonValueKind.Object when current.TryGetProperty("url", out var url) =>
                FetchImageFromUrl(url.GetString()!).GetAwaiter().GetResult(),
            JsonValueKind.Object when current.TryGetProperty("b64_json", out var b64) =>
                Convert.FromBase64String(b64.GetString()!),
            _ => null
        };
    }

    private static async Task<byte[]> FetchImageFromUrl(string url)
    {
        using var http = new HttpClient();
        return await http.GetByteArrayAsync(url);
    }
}
