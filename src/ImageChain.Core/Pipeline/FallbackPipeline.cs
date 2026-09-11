using ImageChain.Core.Abstractions;
using ImageChain.Core.Configuration;
using ImageChain.Core.Logging;
using ImageChain.Core.Models;
using ImageChain.Core.Providers;
using Microsoft.Extensions.Logging;

namespace ImageChain.Core.Pipeline;

public sealed class FallbackPipeline
{
    private readonly ProviderCatalog _catalog;
    private readonly RuntimeConfig _runtime;
    private readonly RequestLogger _requestLogger;
    private readonly ILogger<FallbackPipeline> _logger;

    public FallbackPipeline(
        ProviderCatalog catalog,
        RuntimeConfig runtime,
        RequestLogger requestLogger,
        ILogger<FallbackPipeline> logger)
    {
        _catalog = catalog;
        _runtime = runtime;
        _requestLogger = requestLogger;
        _logger = logger;
    }

    public async Task<ImageGenerationResult> ExecuteAsync(
        BaseTaskRequest request,
        TaskType taskType,
        CancellationToken ct = default)
    {
        var trace = _requestLogger.StartTrace(taskType, request);
        var composition = ResolveComposition(request.Composition);
        var providers = _catalog.ResolveOrder(taskType, composition);

        if (composition is not null)
        {
            _logger.LogInformation("[{TraceId}] 使用调用组合 \"{Composition}\"（{Count} 个模型）",
                trace.TraceId, composition.Name, providers.Count);
        }

        if (providers.Count == 0)
        {
            _requestLogger.CompleteTrace(trace, "NO_PROVIDERS", 0, false);
            throw new AllHandlersFailedException(taskType,
                [new ProviderFailure { ProviderName = "none", ModelId = "none", Error = "No providers support this task type" }]);
        }

        var failures = new List<ProviderFailure>();

        for (var i = 0; i < providers.Count; i++)
        {
            var provider = providers[i];
            var retryCount = provider.GetRetryCount(taskType);
            var success = false;

            for (var attempt = 0; attempt <= retryCount; attempt++)
            {
                if (ct.IsCancellationRequested)
                    break;

                if (attempt == 0)
                    _requestLogger.LogAttempt(trace, provider.VendorName, provider.ModelId, attempt);

                try
                {
                    var result = await provider.GenerateAsync(request, ct);

                    if (result.IsSuccess)
                    {
                        _requestLogger.LogSuccess(trace, provider.VendorName, provider.ModelId);
                        success = true;
                        return result;
                    }

                    _logger.LogWarning("[{Vendor}/{Model}] 第 {Attempt} 次调用失败: {Error}",
                        provider.VendorName, provider.ModelId, attempt + 1, result.ErrorMessage);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{Vendor}/{Model}] 第 {Attempt} 次调用抛异常",
                        provider.VendorName, provider.ModelId, attempt + 1);
                }

                if (attempt < retryCount)
                {
                    _logger.LogInformation("[{Vendor}/{Model}] 正在重试（第 {Next} 次）",
                        provider.VendorName, provider.ModelId, attempt + 2);
                }
                else if (i < providers.Count - 1)
                {
                    _logger.LogInformation("[{Vendor}/{Model}] 已用尽重试，切换到下一个模型 [{NextVendor}/{NextModel}]",
                        provider.VendorName, provider.ModelId, providers[i + 1].VendorName, providers[i + 1].ModelId);
                }
            }

            if (!success)
            {
                failures.Add(new ProviderFailure
                {
                    ProviderName = provider.VendorName,
                    ModelId = provider.ModelId,
                    Error = $"Failed after {retryCount + 1} attempts"
                });
            }
        }

        _requestLogger.CompleteTrace(trace, "ALL_FAILED", failures.Count, false);
        throw new AllHandlersFailedException(taskType, failures);
    }

    private CompositionConfig? ResolveComposition(string? name)
    {
        var requested = string.IsNullOrWhiteSpace(name)
            ? _runtime.Current.ActiveComposition
            : name;
        if (string.IsNullOrWhiteSpace(requested))
            return null;

        return _runtime.Current.Compositions.FirstOrDefault(c =>
            c.Name.Equals(requested, StringComparison.OrdinalIgnoreCase));
    }
}
