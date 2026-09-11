using ImageChain.Core.Models;

namespace ImageChain.Core.Abstractions;

public interface IImageProvider
{
    string VendorName { get; }
    string ModelId { get; }
    string DisplayName { get; }
    bool SupportsTaskType(TaskType taskType);
    int GetPriority(TaskType taskType);
    int GetRetryCount(TaskType taskType);
    Task<ImageGenerationResult> GenerateAsync(BaseTaskRequest request, CancellationToken ct = default);
}
