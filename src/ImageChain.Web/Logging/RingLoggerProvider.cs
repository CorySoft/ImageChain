using Microsoft.Extensions.Logging;

namespace ImageChain.Web.Logging;

public sealed class RingLoggerProvider : ILoggerProvider
{
    private readonly LogRingBuffer _buffer;
    private readonly HashSet<string> _quietCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.AspNetCore.StaticFiles",
        "Microsoft.AspNetCore.Hosting.Diagnostics",
        "Microsoft.Hosting.Lifetime",
        "Microsoft.AspNetCore.Server.Kestrel",
        "Microsoft.AspNetCore.Server.Kestrel.Core",
        "System.Net.Http.HttpClient",
        "Microsoft.Extensions.Http",
        "Microsoft.AspNetCore.Authentication",
        "Microsoft.AspNetCore.Authorization",
        "ImageChain.Web.Auth",
        "ImageChain.Core.Configuration",
        "ImageChain.Core.Providers"
    };

    public RingLoggerProvider(LogRingBuffer buffer)
        => _buffer = buffer;

    public ILogger CreateLogger(string categoryName)
        => new RingLogger(_buffer, categoryName, _quietCategories);

    public void Dispose() { }

    private sealed class RingLogger : ILogger
    {
        private readonly LogRingBuffer _buffer;
        private readonly string _category;
        private readonly HashSet<string> _quiet;

        public RingLogger(LogRingBuffer buffer, string category, HashSet<string> quiet)
        {
            _buffer = buffer;
            _category = category.Length > 60 ? category[..60] : category;
            _quiet = quiet;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            foreach (var quiet in _quiet)
            {
                if (_category.StartsWith(quiet, StringComparison.OrdinalIgnoreCase)) return;
            }

            var level = logLevel switch
            {
                LogLevel.Trace => "Trace",
                LogLevel.Debug => "Debug",
                LogLevel.Information => "Info",
                LogLevel.Warning => "Warn",
                LogLevel.Error => "Error",
                LogLevel.Critical => "Critical",
                _ => "Info"
            };

            var msg = formatter(state, exception);
            if (_category == "Program" && msg.StartsWith("[REQ]", StringComparison.Ordinal))
            {
                var cut = msg.IndexOf("请求体=", StringComparison.Ordinal);
                if (cut >= 0) msg = msg[..cut].TrimEnd();
            }

            var line = $"{DateTime.Now:HH:mm:ss}|{level}|{msg}";
            if (exception is not null && !line.Contains(exception.Message, StringComparison.Ordinal))
                line += $" | {exception.Message}";
            _buffer.Add(line);
        }
    }
}