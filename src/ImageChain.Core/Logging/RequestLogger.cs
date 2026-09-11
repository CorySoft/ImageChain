using System.Diagnostics;
using ImageChain.Core.Abstractions;
using ImageChain.Core.Models;
using Microsoft.Extensions.Logging;

namespace ImageChain.Core.Logging;

public sealed class RequestLogger
{
    private readonly ILogger<RequestLogger> _logger;

    public RequestLogger(ILogger<RequestLogger> logger)
    {
        _logger = logger;
    }

    public RequestTrace StartTrace(TaskType taskType, BaseTaskRequest request)
    {
        var trace = new RequestTrace
        {
            TraceId = Guid.NewGuid().ToString("N")[..12],
            TaskType = taskType,
            StartedAt = Stopwatch.GetTimestamp(),
            Prompt = request switch
            {
                TextToImageRequest t2i => t2i.Prompt,
                ImageToImageRequest i2i => i2i.Prompt,
                _ => ""
            }
        };

        _logger.LogInformation("[{TraceId}] {TaskTypeName}开始，提示词=\"{Prompt}\"",
            trace.TraceId, TaskTypeName(trace.TaskType), Truncate(trace.Prompt, 50));

        return trace;
    }

    public void LogAttempt(RequestTrace trace, string vendor, string modelId, int attempt)
    {
        _logger.LogInformation("[{TraceId}] → 调用模型 {Vendor}/{Model}",
            trace.TraceId, vendor, modelId);
    }

    public void LogSuccess(RequestTrace trace, string vendor, string modelId)
    {
        var elapsed = Stopwatch.GetElapsedTime(trace.StartedAt);
        _logger.LogInformation("[{TraceId}] ✓ 模型 {Vendor}/{Model} 生成成功，耗时 {Elapsed}",
            trace.TraceId, vendor, modelId, Fmt((int)elapsed.TotalMilliseconds));
    }

    public void CompleteTrace(RequestTrace trace, string status, int failureCount, bool success)
    {
        var elapsed = Stopwatch.GetElapsedTime(trace.StartedAt);
        _logger.LogInformation("[{TraceId}] 本次请求结束：状态={Status}，失败模型数={Failures}，总耗时 {Elapsed}",
            trace.TraceId, status, failureCount, Fmt((int)elapsed.TotalMilliseconds));
    }

    private static string Fmt(long ms) => (ms / 1000.0).ToString("0.###") + "s";

    private static string TaskTypeName(TaskType taskType) => taskType switch
    {
        TaskType.TextToImage => "文生图",
        TaskType.ImageToImage => "图生图",
        _ => taskType.ToString()
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}

public sealed class RequestTrace
{
    public string TraceId { get; init; } = "";
    public TaskType TaskType { get; init; }
    public long StartedAt { get; init; }
    public string Prompt { get; init; } = "";
}
