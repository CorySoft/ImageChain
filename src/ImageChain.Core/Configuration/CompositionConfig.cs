namespace ImageChain.Core.Configuration;

public sealed class CompositionConfig
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public List<CompositionModelRef> Models { get; set; } = [];
}

public sealed class CompositionModelRef
{
    public required string Vendor { get; set; }
    public required string ModelId { get; set; }
}