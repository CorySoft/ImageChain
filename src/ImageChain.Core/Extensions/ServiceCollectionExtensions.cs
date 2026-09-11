using ImageChain.Core.Abstractions;
using ImageChain.Core.Configuration;
using ImageChain.Core.Discovery;
using ImageChain.Core.Logging;
using ImageChain.Core.Pipeline;
using ImageChain.Core.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImageChain.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddImageChain(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient();
        services.AddSingleton<RequestLogger>();

        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ConfigStore>>();
            var configPath = Path.Combine(AppContext.BaseDirectory, "imagechain-config.json");
            if (File.Exists(configPath))
                return new ConfigStore(configPath, logger);

            var altPath = Path.Combine(Directory.GetCurrentDirectory(), "imagechain-config.json");
            return new ConfigStore(altPath, logger);
        });

        services.AddSingleton<RuntimeConfig>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<RuntimeConfig>>();
            var configStore = sp.GetRequiredService<ConfigStore>();
            var seed = configuration.GetSection("ImageChain").Get<ImageChainOptions>() ?? new ImageChainOptions();
            configStore.EnsureSeeded(seed);
            var options = configStore.LoadAsync().GetAwaiter().GetResult();
            return new RuntimeConfig(options, logger);
        });

        services.AddSingleton<IOptions<ImageChainOptions>, RuntimeOptions>();
        services.AddSingleton<IOptionsMonitor<ImageChainOptions>, RuntimeOptionsMonitor>();

        services.TryAddSingleton<IVendorModelDiscovery, OpenAIModelDiscovery>();
        services.TryAddSingleton<IVendorModelDiscovery, HunyuanModelDiscovery>();
        services.TryAddSingleton<IVendorModelDiscovery, WanxModelDiscovery>();

        services.AddSingleton<ProviderCatalog>();
        services.AddSingleton<FallbackPipeline>();

        return services;
    }

}