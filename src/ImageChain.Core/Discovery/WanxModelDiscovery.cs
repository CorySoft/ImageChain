using System.Text.Json;

namespace ImageChain.Core.Discovery;

public sealed class WanxModelDiscovery : IVendorModelDiscovery
{
    public string VendorName => "AlibabaDashScope";

    public async Task<IReadOnlyList<DiscoveredModel>> DiscoverModelsAsync(
        string apiKey, string endpoint, CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

        var url = DiscoveryUrl.Models(endpoint, "capabilities=IG&page_size=100");
        var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var models = new List<DiscoveredModel>();

        if (doc.RootElement.TryGetProperty("output", out var output) &&
            output.TryGetProperty("models", out var modelList))
        {
            foreach (var item in modelList.EnumerateArray())
            {
                var modelId = item.GetProperty("model").GetString() ?? "";
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : modelId;
                var description = item.TryGetProperty("description", out var d) ? d.GetString() : null;

                var capabilities = new List<string> { "TextToImage" };
                if (item.TryGetProperty("inference_metadata", out var meta))
                {
                    if (meta.TryGetProperty("request_modality", out var reqMod))
                    {
                        foreach (var mod in reqMod.EnumerateArray())
                        {
                            if (mod.GetString() == "Image")
                            {
                                capabilities.Add("ImageToImage");
                                break;
                            }
                        }
                    }
                }

                models.Add(new DiscoveredModel
                {
                    Id = modelId,
                    DisplayName = name ?? modelId,
                    Description = description,
                    Vendor = VendorName,
                    Capabilities = capabilities
                });
            }
        }

        return models;
    }
}
