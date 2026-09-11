using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace ImageChain.Core.Configuration;

public sealed class RuntimeOptions : IOptions<ImageChainOptions>
{
    private readonly RuntimeConfig _runtime;

    public RuntimeOptions(RuntimeConfig runtime)
    {
        _runtime = runtime;
    }

    public ImageChainOptions Value => _runtime.Current;
}

public sealed class RuntimeOptionsMonitor : IOptionsMonitor<ImageChainOptions>, IDisposable
{
    private readonly RuntimeConfig _runtime;
    private readonly List<IDisposable> _registrations = [];

    public RuntimeOptionsMonitor(RuntimeConfig runtime)
    {
        _runtime = runtime;
    }

    public ImageChainOptions CurrentValue => _runtime.Current;

    public ImageChainOptions Get(string? name) => _runtime.Current;

    public IDisposable OnChange(Action<ImageChainOptions, string?> listener)
    {
        var registration = ChangeToken.OnChange(_runtime.GetChangeToken, () => listener(_runtime.Current, null));
        _registrations.Add(registration);
        return registration;
    }

    public void Dispose()
    {
        foreach (var registration in _registrations)
            registration.Dispose();
    }
}