using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace NexJob.Dashboard.Pages;

/// <summary>
/// Provides in-memory host process metrics (CPU and RAM working set) for the current node
/// without persisting volatile transient telemetry into disk or database tables.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class HostSystemMetrics
{
    private static readonly object SyncLock = new();
    private static DateTimeOffset _lastCpuSampleTime = DateTimeOffset.UtcNow;
    private static TimeSpan _lastTotalProcessorTime = GetCurrentProcessorTime();
    private static int _lastCpuPercent;

    private static TimeSpan GetCurrentProcessorTime()
    {
        try
        {
            using var proc = Process.GetCurrentProcess();
            return proc.TotalProcessorTime;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Gets in-memory process health metrics (CPU % and RAM working set) for the current host process.
    /// </summary>
    internal static (int CpuPercent, int WorkingSetMb, int MemoryPercent, long ManagedHeapBytes) GetCurrent()
    {
        var cpu = _lastCpuPercent;
        long workingSetBytes = 0;
        long totalAvailableBytes = 0;
        long managedHeapBytes = 0;

        try
        {
            using var proc = Process.GetCurrentProcess();
            workingSetBytes = proc.WorkingSet64;

            var now = DateTimeOffset.UtcNow;
            lock (SyncLock)
            {
                var totalProcTime = proc.TotalProcessorTime;
                var wallClockMs = (now - _lastCpuSampleTime).TotalMilliseconds;
                var procTimeMs = (totalProcTime - _lastTotalProcessorTime).TotalMilliseconds;

                // Sample every >= 300ms to calculate an accurate delta
                if (wallClockMs >= 300)
                {
                    var cores = Environment.ProcessorCount;
                    if (cores > 0 && wallClockMs > 0)
                    {
                        var rawPercent = (procTimeMs / (wallClockMs * cores)) * 100.0;
                        _lastCpuPercent = Math.Clamp((int)Math.Round(rawPercent), 0, 100);
                    }

                    _lastCpuSampleTime = now;
                    _lastTotalProcessorTime = totalProcTime;
                }

                cpu = _lastCpuPercent;
            }
        }
        catch
        {
            cpu = 0;
        }

        try
        {
            managedHeapBytes = GC.GetTotalMemory(false);
            var memInfo = GC.GetGCMemoryInfo();
            totalAvailableBytes = memInfo.TotalAvailableMemoryBytes;
            if (totalAvailableBytes <= 0)
            {
                totalAvailableBytes = 1024L * 1024L * 1024L * 4; // 4GB default container baseline
            }
        }
        catch
        {
            totalAvailableBytes = 1024L * 1024L * 1024L * 4;
        }

        var workingSetMb = (int)(workingSetBytes / (1024 * 1024));
        var memPercent = Math.Clamp((int)Math.Round((double)workingSetBytes / totalAvailableBytes * 100.0), 1, 100);

        return (cpu, workingSetMb, memPercent, managedHeapBytes);
    }
}
