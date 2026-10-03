namespace NexJob.ReliabilityTests;

/// <summary>
/// In the crash worker (a <see cref="CrashMarker"/> is registered) it announces itself and hangs. Anywhere else it just
/// records that it ran, which is what the recovering host does.
/// </summary>
internal sealed class CrashTargetJob : IJob
{
    private readonly ExecutionLog _log;
    private readonly IJobContext _context;
    private readonly CrashMarker? _marker;

    public CrashTargetJob(ExecutionLog log, IJobContext context, CrashMarker? marker = null)
    {
        _log = log;
        _context = context;
        _marker = marker;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (_marker is not null)
        {
            await File.WriteAllTextAsync(_marker.Path, _context.JobId.Value.ToString(), cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        _log.Add($"done:{_context.JobId.Value}");
    }
}
