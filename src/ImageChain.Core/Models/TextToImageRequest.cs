namespace ImageChain.Core.Models;

public sealed class TextToImageRequest : BaseTaskRequest
{
    public required string Prompt { get; init; }
    public int Width { get; init; } = 1024;
    public int Height { get; init; } = 1024;
    public int Count { get; init; } = 1;
}
