using System.Diagnostics;
using System.Globalization;

namespace AI.API.Diagnostics;

internal sealed class AssistantTiming(ILogger logger, string stage) : IDisposable
{
    private readonly Stopwatch watch = Stopwatch.StartNew();
    private readonly string traceId = Activity.Current?.TraceId.ToString() ?? "none";

    public void Dispose()
    {
        if (!watch.IsRunning)
        {
            return;
        }

        watch.Stop();
        logger.LogInformation("AITiming TraceId={TraceId} Stage={Stage} Milliseconds={Milliseconds}",
            traceId, stage, watch.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture));
    }
}
