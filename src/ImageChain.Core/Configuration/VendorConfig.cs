namespace ImageChain.Core.Configuration;

public sealed class VendorConfig
{
    public required string Name { get; set; }
    public required string ApiKey { get; set; }
    public required string BaseEndpoint { get; set; }
    public string? DiscoveryEndpoint { get; set; }
    public string AuthType { get; set; } = "Bearer";
    public string ApiKeyHeader { get; set; } = "Authorization";
    public string ApiKeyPrefix { get; set; } = "Bearer ";
    public List<ModelConfig> Models { get; set; } = [];
}
