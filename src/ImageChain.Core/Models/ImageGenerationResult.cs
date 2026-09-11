namespace ImageChain.Core.Models;

public sealed class ImageGenerationResult
{
    public bool IsSuccess { get; init; }
    public byte[]? ImageData { get; init; }
    public string MediaType { get; init; } = "image/png";
    public string? ProviderName { get; init; }
    public string? ModelId { get; init; }
    public string? ErrorMessage { get; init; }
    public IDictionary<string, object?>? Metadata { get; init; }

    public static ImageGenerationResult Success(byte[] data, string provider, string modelId, string mediaType = "image/png")
        => new()
        {
            IsSuccess = true,
            ImageData = data,
            ProviderName = provider,
            ModelId = modelId,
            MediaType = mediaType
        };

    public static ImageGenerationResult Failure(string provider, string modelId, string error)
        => new()
        {
            IsSuccess = false,
            ProviderName = provider,
            ModelId = modelId,
            ErrorMessage = error
        };
}
