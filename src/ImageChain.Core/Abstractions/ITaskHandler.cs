using ImageChain.Core.Models;

namespace ImageChain.Core.Abstractions;

public interface ITaskHandler<in TRequest, TResponse> where TRequest : BaseTaskRequest
{
    string Name { get; }
    bool SupportsTaskType(TaskType taskType);
    int GetPriority(TaskType taskType);
    Task<TResponse> HandleAsync(TRequest request, CancellationToken ct = default);
}
