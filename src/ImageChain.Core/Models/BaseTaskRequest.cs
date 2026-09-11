namespace ImageChain.Core.Models;

public abstract class BaseTaskRequest
{
    public string? MediaType { get; init; }
    public IDictionary<string, object?>? ProviderParameters { get; init; }
    public string? Composition { get; init; }
}
