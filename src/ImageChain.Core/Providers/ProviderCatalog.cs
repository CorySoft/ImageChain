using ImageChain.Core.Abstractions;
using ImageChain.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImageChain.Core.Providers;

public sealed class ProviderCatalog : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProviderCatalog> _logger;
    private readonly IDisposable? _registration;
    private readonly object _lock = new();
    private IReadOnlyList<IImageProvider> _providers = [];

    public ProviderCatalog(
        IServiceProvider serviceProvider,
        IOptionsMonitor<ImageChainOptions> optionsMonitor,
        ILogger<ProviderCatalog> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _providers = BuildProviders(optionsMonitor.CurrentValue);
        _registration = optionsMonitor.OnChange((options, _) =>
        {
            lock (_lock)
            {
                _providers = BuildProviders(options);
            }
            _logger.LogInformation("Provider catalog refreshed from config change");
        });
    }

    public IReadOnlyList<IImageProvider> Providers
    {
        get
        {
            lock (_lock)
            {
                return _providers;
            }
        }
    }

    private IReadOnlyList<IImageProvider> BuildProviders(ImageChainOptions options)
    {
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();

        var list = new List<IImageProvider>();
        foreach (var vendor in options.Vendors)
        {
            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = Timeout.InfiniteTimeSpan;

            foreach (var model in vendor.Models.Where(m => m.Enabled))
            {
                var providerLogger = loggerFactory.CreateLogger<CustomHttpImageProvider>();
                list.Add(new CustomHttpImageProvider(httpClient, vendor, model, providerLogger));
            }
        }

        return list;
    }

    public void Dispose()
    {
        _registration?.Dispose();
    }
}