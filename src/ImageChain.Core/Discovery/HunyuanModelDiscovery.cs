using System.Text.Json;

namespace ImageChain.Core.Discovery;

public sealed class HunyuanModelDiscovery : IVendorModelDiscovery
{
    public string VendorName => "TencentHunyuan";

    public async Task<IReadOnlyList<DiscoveredModel>> DiscoverModelsAsync(
        string apiKey, string endpoint, CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

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
                if (id.Contains("image", StringComparison.OrdinalIgnoreCase) ||
                    id.Contains("hy-", StringComparison.OrdinalIgnoreCase))
                {
                    models.Add(new DiscoveredModel
                    {
                        Id = id,
                        DisplayName = id,
                        Vendor = VendorName,
                        Capabilities = ["TextToImage", "ImageToImage"]
                    });
                }
            }
        }

        return models;
    }
}
