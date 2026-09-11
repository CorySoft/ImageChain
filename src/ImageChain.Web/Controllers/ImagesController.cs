using System.Globalization;
using System.Text;
using System.Text.Json;
using ImageChain.Core.Abstractions;
using ImageChain.Core.Models;
using ImageChain.Core.Pipeline;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ImageChain.Web.Controllers;

[ApiController]
[Route("v1")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public class ImagesController : ControllerBase
{
    public const string GenImgModel = "GenImg";
    public const string EditImgModel = "EditImg";

    private static readonly string[] GenImgAliases =
        ["genimg", "genimage", "gen-img", "generate-image", "text2img", "text-to-image", "t2i"];

    private static readonly string[] EditImgAliases =
        ["editimg", "editimage", "edit-image", "img2img", "image-to-image", "i2i", "transform", "inpainting", "outpainting"];

    private readonly FallbackPipeline _pipeline;

    public ImagesController(FallbackPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    private static bool IsGenImgFamily(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return true;
        var m = model.Trim();
        return m.Equals(GenImgModel, StringComparison.OrdinalIgnoreCase)
               || GenImgAliases.Contains(m, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsEditImgFamily(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return true;
        var m = model.Trim();
        return m.Equals(EditImgModel, StringComparison.OrdinalIgnoreCase)
               || EditImgAliases.Contains(m, StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetQueryString(IQueryCollection query, params string[] names)
    {
        foreach (var name in names)
        {
            if (query.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.ToString();
        }
        return null;
    }

    private async Task<ImageGenerationRequest> ReadGenerationRequestAsync()
    {
        var request = new ImageGenerationRequest();

        Request.EnableBuffering();
        string raw;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true, detectEncodingFromByteOrderMarks: false))
            raw = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    request.Model = GetStringProp(root, "model");
                    request.Prompt = GetStringProp(root, "prompt") ?? "";
                    request.N = GetIntProp(root, "n", "count");
                    request.Size = GetStringProp(root, "size");
                    request.ResponseFormat = GetStringProp(root, "response_format", "responseFormat");
                    request.Quality = GetStringProp(root, "quality");
                    request.Style = GetStringProp(root, "style");
                    request.Composition = GetStringProp(root, "composition");
                    request.AdditionalProperties = ExtractAdditionalProperties(root);
                }
            }
            catch (JsonException) { }
        }

        if (request.Model is null && Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            request.Model = (string?)form["model"];
            request.Prompt = string.IsNullOrEmpty(request.Prompt) ? ((string?)form["prompt"] ?? "") : request.Prompt;
            request.N ??= int.TryParse((string?)form["n"], out var fn) ? fn : null;
            request.N ??= int.TryParse((string?)form["count"], out var fc) ? fc : null;
            request.Size = request.Size ?? (string?)form["size"];
            request.ResponseFormat = request.ResponseFormat ?? (string?)form["response_format"] ?? (string?)form["responseFormat"];
        }

        request.Model = request.Model ?? GetQueryString(Request.Query, "model") ?? GenImgModel;
        if (string.IsNullOrWhiteSpace(request.Prompt))
            request.Prompt = GetQueryString(Request.Query, "prompt") ?? "a beautiful landscape, mountains and a lake, highly detailed digital art";
        request.Size = request.Size ?? GetQueryString(Request.Query, "size");
        request.ResponseFormat = request.ResponseFormat ?? GetQueryString(Request.Query, "response_format", "responseFormat");
        request.Composition = request.Composition ?? ReadComposition(Request);
        return request;
    }

    private static readonly HashSet<string> KnownBodyProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "model", "prompt", "n", "count", "size", "response_format", "responseFormat",
        "quality", "style", "image", "strength", "composition"
    };

    private static string? ReadComposition(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-ImageChain-Composition", out var header) &&
            !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString();
        }
        if (request.Query.TryGetValue("composition", out var query) && !string.IsNullOrWhiteSpace(query))
            return query.ToString();
        return null;
    }

    private static Dictionary<string, object?>? ExtractAdditionalProperties(JsonElement root)
    {
        Dictionary<string, object?>? dict = null;
        foreach (var prop in root.EnumerateObject())
        {
            if (KnownBodyProperties.Contains(prop.Name)) continue;
            (dict ??= new Dictionary<string, object?>())[prop.Name] = prop.Value.Clone();
        }
        return dict;
    }

    private static string? GetStringProp(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetProperty(name, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.String) return prop.GetString() ?? "";
            if (prop.ValueKind == JsonValueKind.Number) return prop.GetRawText();
            if (prop.ValueKind == JsonValueKind.Object &&
                prop.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                return type.GetString();
        }
        return null;
    }

    private static int? GetIntProp(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetProperty(name, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var i)) return i;
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var s)) return s;
        }
        return null;
    }

    private static float? GetFloatProp(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetProperty(name, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetSingle(out var f)) return f;
            if (prop.ValueKind == JsonValueKind.String &&
                float.TryParse(prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var sf))
                return sf;
        }
        return null;
    }

    private async Task<ImageTransformRequest> ReadTransformRequestAsync()
    {
        var request = new ImageTransformRequest();

        Request.EnableBuffering();
        string raw;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true, detectEncodingFromByteOrderMarks: false))
            raw = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    request.Model = GetStringProp(root, "model");
                    request.Prompt = GetStringProp(root, "prompt") ?? "";
                    request.Image = GetStringProp(root, "image");
                    request.Strength = GetFloatProp(root, "strength");
                    request.Size = GetStringProp(root, "size");
                    request.ResponseFormat = GetStringProp(root, "response_format", "responseFormat");
                    request.Composition = GetStringProp(root, "composition");
                    request.AdditionalProperties = ExtractAdditionalProperties(root);
                }
            }
            catch (JsonException) { }
        }

        if (request.Model is null && Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            request.Model = (string?)form["model"];
            request.Prompt = string.IsNullOrEmpty(request.Prompt) ? ((string?)form["prompt"] ?? "") : request.Prompt;
            request.Image = request.Image ?? (string?)form["image"];
            request.Strength = request.Strength ?? (float.TryParse((string?)form["strength"], out var s) ? s : null);
            request.Size = request.Size ?? (string?)form["size"];
            request.ResponseFormat = request.ResponseFormat ?? (string?)form["response_format"] ?? (string?)form["responseFormat"];
        }

        request.Model = request.Model ?? GetQueryString(Request.Query, "model") ?? EditImgModel;
        if (string.IsNullOrWhiteSpace(request.Prompt))
            request.Prompt = GetQueryString(Request.Query, "prompt") ?? "a beautiful landscape, mountains and a lake, highly detailed digital art";
        request.Image = request.Image ?? GetQueryString(Request.Query, "image");
        request.Size = request.Size ?? GetQueryString(Request.Query, "size");
        request.ResponseFormat = request.ResponseFormat ?? GetQueryString(Request.Query, "response_format", "responseFormat");
        request.Composition = request.Composition ?? ReadComposition(Request);
        return request;
    }

    public sealed class ImageGenerationRequest
    {
        public string? Model { get; set; }
        public string Prompt { get; set; } = "";
        public int? N { get; set; }
        public string? Size { get; set; }
        public string? ResponseFormat { get; set; }
        public string? Quality { get; set; }
        public string? Style { get; set; }
        public string? Composition { get; set; }
        public Dictionary<string, object?>? AdditionalProperties { get; set; }
    }

    public sealed class ImageTransformRequest
    {
        public string? Model { get; set; }
        public string Prompt { get; set; } = "";
        public string? Image { get; set; }
        public float? Strength { get; set; }
        public string? Size { get; set; }
        public string? ResponseFormat { get; set; }
        public string? Composition { get; set; }
        public Dictionary<string, object?>? AdditionalProperties { get; set; }
    }

    [HttpPost("images/generations")]
    [HttpPost("genimage")]
    public async Task<IActionResult> GenerateImage()
    {
        var request = await ReadGenerationRequestAsync();

        if (!IsGenImgFamily(request.Model) && IsEditImgFamily(request.Model))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_MODEL",
                    message = $"模型 '{request.Model}' 不适用图片生成，请使用 '{GenImgModel}'（生图）"
                }
            });
        }

        var (width, height) = ParseSize(request.Size);

        var providerParams = new Dictionary<string, object?>();
        if (request.AdditionalProperties is not null)
        {
            foreach (var kv in request.AdditionalProperties)
                providerParams[kv.Key] = kv.Value;
        }
        if (!string.IsNullOrEmpty(request.Quality))
            providerParams["quality"] = request.Quality;
        if (!string.IsNullOrEmpty(request.Style))
            providerParams["style"] = request.Style;

        var t2iRequest = new TextToImageRequest
        {
            Prompt = request.Prompt,
            Width = width,
            Height = height,
            Count = request.N ?? 1,
            Composition = request.Composition,
            ProviderParameters = providerParams
        };

        try
        {
            var result = await _pipeline.ExecuteAsync(t2iRequest, TaskType.TextToImage);

            if (request.ResponseFormat == "b64_json")
            {
                return Ok(new
                {
                    created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    data = new[]
                    {
                        new { b64_json = Convert.ToBase64String(result.ImageData!) }
                    }
                });
            }

            return Ok(new
            {
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                data = new[]
                {
                    new { url = $"data:{result.MediaType};base64,{Convert.ToBase64String(result.ImageData!)}" }
                }
            });
        }
        catch (AllHandlersFailedException ex)
        {
            return StatusCode(503, new
            {
                error = new
                {
                    code = "ALL_PROVIDERS_FAILED",
                    message = ex.Message,
                    details = ex.Failures.Select(f => new { f.ProviderName, f.ModelId, f.Error })
                }
            });
        }
    }

    [HttpPost("images/transform")]
    [HttpPost("editimage")]
    public async Task<IActionResult> TransformImage()
    {
        var request = await ReadTransformRequestAsync();

        if (!IsEditImgFamily(request.Model) && IsGenImgFamily(request.Model))
        {
            return BadRequest(new
            {
                error = new
                {
                    code = "INVALID_MODEL",
                    message = $"模型 '{request.Model}' 不适用图像转换，请使用 '{EditImgModel}'（以图生图）"
                }
            });
        }

        if (string.IsNullOrEmpty(request.Image))
            return BadRequest(new { error = "Image is required for image-to-image" });

        byte[] imageBytes;
        string mediaType = "image/png";

        if (request.Image.StartsWith("data:"))
        {
            var parts = request.Image.Split(',');
            if (parts.Length == 2)
            {
                mediaType = parts[0].Split(';')[0].Replace("data:", "");
                imageBytes = Convert.FromBase64String(parts[1]);
            }
            else
            {
                return BadRequest(new { error = "Invalid base64 data URI" });
            }
        }
        else if (request.Image.StartsWith("http"))
        {
            using var http = new HttpClient();
            imageBytes = await http.GetByteArrayAsync(request.Image);
        }
        else
        {
            imageBytes = Convert.FromBase64String(request.Image);
        }

        var providerParams = new Dictionary<string, object?>();
        if (request.AdditionalProperties is not null)
        {
            foreach (var kv in request.AdditionalProperties)
                providerParams[kv.Key] = kv.Value;
        }

        var i2iRequest = new ImageToImageRequest
        {
            Prompt = request.Prompt,
            SourceImage = imageBytes,
            SourceMediaType = mediaType,
            Strength = request.Strength,
            Composition = request.Composition,
            ProviderParameters = providerParams
        };

        try
        {
            var result = await _pipeline.ExecuteAsync(i2iRequest, TaskType.ImageToImage);

            if (request.ResponseFormat == "b64_json")
            {
                return Ok(new
                {
                    created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    data = new[]
                    {
                        new { b64_json = Convert.ToBase64String(result.ImageData!) }
                    }
                });
            }

            return Ok(new
            {
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                data = new[]
                {
                    new { url = $"data:{result.MediaType};base64,{Convert.ToBase64String(result.ImageData!)}" }
                }
            });
        }
        catch (AllHandlersFailedException ex)
        {
            return StatusCode(503, new
            {
                error = new
                {
                    code = "ALL_PROVIDERS_FAILED",
                    message = ex.Message,
                    details = ex.Failures.Select(f => new { f.ProviderName, f.ModelId, f.Error })
                }
            });
        }
    }

    [HttpGet("models")]
    public IActionResult ListModels()
    {
        return Ok(new Dictionary<string, object>
        {
            ["object"] = "list",
            ["data"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["id"] = GenImgModel,
                    ["object"] = "model",
                    ["owned_by"] = "imagechain",
                    ["type"] = "text-to-image",
                    ["description"] = "生图",
                    ["input_modalities"] = new[] { "text" },
                    ["output_modalities"] = new[] { "image" }
                },
                new Dictionary<string, object>
                {
                    ["id"] = EditImgModel,
                    ["object"] = "model",
                    ["owned_by"] = "imagechain",
                    ["type"] = "image-to-image",
                    ["description"] = "以图生图",
                    ["input_modalities"] = new[] { "text", "image" },
                    ["output_modalities"] = new[] { "image" }
                }
            }
        });
    }

    private static (int width, int height) ParseSize(string? size)
    {
        if (string.IsNullOrEmpty(size)) return (1024, 1024);

        if (size.Contains('x'))
        {
            var parts = size.Split('x');
            if (int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
                return (w, h);
        }

        if (size.Contains('*'))
        {
            var parts = size.Split('*');
            if (int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
                return (w, h);
        }

        return size switch
        {
            "256x256" => (256, 256),
            "512x512" => (512, 512),
            "1024x1024" => (1024, 1024),
            "1792x1024" => (1792, 1024),
            "1024x1792" => (1024, 1792),
            _ => (1024, 1024)
        };
    }
}
