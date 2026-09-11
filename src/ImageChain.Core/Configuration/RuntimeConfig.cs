using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace ImageChain.Core.Configuration;

public sealed class RuntimeConfig : IDisposable
{
    private ImageChainOptions _current;
    private CancellationTokenSource _cts = new();
    private readonly object _lock = new();
    private readonly ILogger<RuntimeConfig> _logger;

    public RuntimeConfig(ImageChainOptions initial, ILogger<RuntimeConfig> logger)
    {
        _current = initial;
        _logger = logger;
    }

    public ImageChainOptions Current => _current;

    public IChangeToken GetChangeToken() => new CancellationChangeToken(_cts.Token);

    public void Reload(ImageChainOptions next)
    {
        lock (_lock)
        {
            _current = next;
        }
        Interlocked.Exchange(ref _cts, new CancellationTokenSource()).Cancel();
        _logger.LogInformation("配置已重新加载 v{Version}", next.ConfigVersion);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}