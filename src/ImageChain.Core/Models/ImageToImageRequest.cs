namespace ImageChain.Core.Models;

public sealed class ImageToImageRequest : BaseTaskRequest
{
    public required string Prompt { get; init; }
    public required byte[] SourceImage { get; init; }
    public string SourceMediaType { get; init; } = "image/png";
    public float? Strength { get; init; }
}
