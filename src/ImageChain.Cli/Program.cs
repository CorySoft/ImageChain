using System.CommandLine;
using ImageChain.Core.Abstractions;
using ImageChain.Core.Configuration;
using ImageChain.Core.Extensions;
using ImageChain.Core.Models;
using ImageChain.Core.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

#pragma warning disable MEAI001

var rootCommand = new RootCommand("ImageChain CLI - Image Generation Fallback");

var promptOption = new Option<string>("--prompt", "Image generation prompt") { IsRequired = true };
var sizeOption = new Option<string>("--size", () => "1024x1024", "Image size (e.g. 1024x1024)");
var outputOption = new Option<string>("--output", "Output file path (prints base64 if not set)");
var typeOption = new Option<string>("--type", () => "text-to-image", "Task type: text-to-image or image-to-image");
var imageOption = new Option<string>("--image", "Source image path for image-to-image");
var configOption = new Option<string>("--config", () => "appsettings.json", "Config file path");

rootCommand.AddOption(promptOption);
rootCommand.AddOption(sizeOption);
rootCommand.AddOption(outputOption);
rootCommand.AddOption(typeOption);
rootCommand.AddOption(imageOption);
rootCommand.AddOption(configOption);

rootCommand.SetHandler(async (prompt, size, output, type, image, configPath) =>
{
    var builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddJsonFile(configPath, optional: true, reloadOnChange: false);
    builder.Services.AddImageChain(builder.Configuration);
    builder.Services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

    var host = builder.Build();
    var pipeline = host.Services.GetRequiredService<FallbackPipeline>();

    var (width, height) = ParseSize(size);

    Console.WriteLine($"Prompt: {prompt}");
    Console.WriteLine($"Size: {width}x{height}");
    Console.WriteLine($"Type: {type}");
    Console.WriteLine("---");

    try
    {
        ImageGenerationResult result;

        if (type == "image-to-image" && !string.IsNullOrEmpty(image))
        {
            var imageBytes = File.ReadAllBytes(image);
            var mediaType = image.EndsWith(".png") ? "image/png" : "image/jpeg";
            var req = new ImageToImageRequest
            {
                Prompt = prompt,
                SourceImage = imageBytes,
                SourceMediaType = mediaType
            };
            result = await pipeline.ExecuteAsync(req, TaskType.ImageToImage);
        }
        else
        {
            var req = new TextToImageRequest
            {
                Prompt = prompt,
                Width = width,
                Height = height
            };
            result = await pipeline.ExecuteAsync(req, TaskType.TextToImage);
        }

        Console.WriteLine($"Provider: {result.ProviderName}/{result.ModelId}");
        Console.WriteLine($"Image: {result.ImageData!.Length} bytes ({result.MediaType})");

        if (!string.IsNullOrEmpty(output))
        {
            await File.WriteAllBytesAsync(output, result.ImageData);
            Console.WriteLine($"Saved to: {output}");
        }
        else
        {
            Console.WriteLine($"Base64: {Convert.ToBase64String(result.ImageData).Length} chars");
        }
    }
    catch (AllHandlersFailedException ex)
    {
        Console.Error.WriteLine($"All providers failed: {ex.Message}");
        foreach (var f in ex.Failures)
            Console.Error.WriteLine($"  - {f.ProviderName}/{f.ModelId}: {f.Error}");
        Environment.Exit(1);
    }
}, promptOption, sizeOption, outputOption, typeOption, imageOption, configOption);

return await rootCommand.InvokeAsync(args);

static (int w, int h) ParseSize(string size)
{
    var sep = size.Contains('x') ? 'x' : '*';
    var parts = size.Split(sep);
    if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
        return (w, h);
    return (1024, 1024);
}
