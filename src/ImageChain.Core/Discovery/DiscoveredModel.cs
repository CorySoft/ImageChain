namespace ImageChain.Core.Discovery;

public sealed class DiscoveredModel
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public string? Vendor { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = [];
}
