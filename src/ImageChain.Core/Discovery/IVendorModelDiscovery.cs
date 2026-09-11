namespace ImageChain.Core.Discovery;

public interface IVendorModelDiscovery
{
    string VendorName { get; }
    Task<IReadOnlyList<DiscoveredModel>> DiscoverModelsAsync(string apiKey, string endpoint, CancellationToken ct = default);
}

internal static class DiscoveryUrl
{
    public static string Models(string endpoint, string? query = null)
    {
        var trimmed = endpoint.TrimEnd('/').Split('?')[0].TrimEnd('/');
        string baseUrl;
        if (trimmed.EndsWith("/v1/models", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("/api/v1/models", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = trimmed;
        }
        else
        {
            baseUrl = $"{trimmed}/v1/models";
        }

        return string.IsNullOrEmpty(query) ? baseUrl : $"{baseUrl}?{query.TrimStart('?')}";
    }
}
