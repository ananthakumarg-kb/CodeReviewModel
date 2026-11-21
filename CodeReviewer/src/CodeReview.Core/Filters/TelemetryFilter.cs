using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace CodeReview.Core.Filters;

/// <summary>
/// IFunctionInvocationFilter = middleware around every KernelFunction call (prompt or native).
/// Used for latency, token usage and error logging, the hooks you need for cost control and audit.
/// </summary>
public sealed class TelemetryFilter : IFunctionInvocationFilter
{
    private readonly ILogger _log;

    public TelemetryFilter(ILogger<TelemetryFilter> log) => _log = log;

    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await next(context);
            object? usage = null;
            context.Result.Metadata?.TryGetValue("Usage", out usage);
            _log.LogInformation("SK function {Plugin}.{Function} ok in {Ms} ms. Usage: {Usage}",
                context.Function.PluginName ?? "-", context.Function.Name, sw.ElapsedMilliseconds, usage ?? "n/a");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SK function {Function} failed after {Ms} ms", context.Function.Name, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
