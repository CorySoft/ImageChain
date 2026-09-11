namespace ImageChain.Core.Configuration;

public sealed class ModelConfig
{
    public required string Id { get; set; }
    public string? DisplayName { get; set; }
    public bool Enabled { get; set; } = true;
    public int RetryCount { get; set; } = 1;
    public Dictionary<string, CapabilityConfig> Capabilities { get; set; } = [];
    public Dictionary<string, object?>? DefaultParameters { get; set; }
    public Dictionary<string, object?>? RequestTemplate { get; set; }
    public string? ResponseImagePath { get; set; }
}

public sealed class CapabilityConfig
{
    public int Priority { get; set; } = 10;
}
