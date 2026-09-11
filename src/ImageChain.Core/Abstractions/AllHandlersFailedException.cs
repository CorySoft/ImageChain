namespace ImageChain.Core.Abstractions;

public sealed class AllHandlersFailedException : Exception
{
    public TaskType TaskType { get; }
    public IReadOnlyList<ProviderFailure> Failures { get; }

    public AllHandlersFailedException(TaskType taskType, IReadOnlyList<ProviderFailure> failures)
        : base($"All providers failed for {taskType}: {string.Join(", ", failures.Select(f => f.ProviderName))}")
    {
        TaskType = taskType;
        Failures = failures;
    }
}

public sealed class ProviderFailure
{
    public required string ProviderName { get; init; }
    public required string ModelId { get; init; }
    public required string Error { get; init; }
}
