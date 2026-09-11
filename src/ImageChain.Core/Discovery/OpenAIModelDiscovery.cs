using System.Text.Json;

namespace ImageChain.Core.Discovery;

public sealed class OpenAIModelDiscovery : IVendorModelDiscovery
{
    public string VendorName => "OpenAI";

    private static readonly string[] ImageModelPrefixes = ["dall-e", "gpt-image", "gpt-image-1", "image"];

    public async Task<IReadOnlyList<DiscoveredModel>> DiscoverModelsAsync(
        string apiKey, string endpoint, CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        http.Timeout = TimeSpan.FromSeconds(120);

        var url = DiscoveryUrl.Models(endpoint);
        var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var models = new List<DiscoveredModel>();
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? "";
                if (!IsImageModel(item, id))
                    continue;

                var capabilities = new List<string> { "TextToImage" };
                if (HasInputModality(item, "image"))
                    capabilities.Add("ImageToImage");

                models.Add(new DiscoveredModel
                {
                    Id = id,
                    DisplayName = id,
                    Vendor = VendorName,
                    Capabilities = capabilities
                });
            }
        }

        return models;
    }

    private static bool IsImageModel(JsonElement item, string id)
    {
        if (ImageModelPrefixes.Any(p => id.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (item.TryGetProperty("type", out var type))
        {
            var typeValue = type.GetString();
            if (typeValue?.Equals("image", StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        if (item.TryGetProperty("output_modalities", out var outputModalities) &&
            outputModalities.ValueKind == JsonValueKind.Array)
        {
            foreach (var mod in outputModalities.EnumerateArray())
            {
                if (mod.GetString()?.Equals("image", StringComparison.OrdinalIgnoreCase) == true)
                    return true;
            }
        }

        return false;
    }

    private static bool HasInputModality(JsonElement item, string target)
    {
        if (!item.TryGetProperty("input_modalities", out var inputModalities) ||
            inputModalities.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var mod in inputModalities.EnumerateArray())
        {
            if (mod.GetString()?.Equals(target, StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        return false;
    }
}