namespace ImageChain.Core.Configuration;

public sealed class ImageChainOptions
{
    public string AdminPassword { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int ConfigVersion { get; set; } = 1;
    public List<VendorConfig> Vendors { get; set; } = [];
    public List<CompositionConfig> Compositions { get; set; } = [];
    public string? ActiveComposition { get; set; }
}
