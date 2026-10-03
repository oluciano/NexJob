namespace NexJob.ReliabilityTests;

/// <summary>
/// A job that records the moment it runs, tagged with its own id, so order and identity can be asserted.
/// </summary>
internal sealed class StepJob : IJob
{
    private readonly ExecutionLog _log;
    private readonly IJobContext _context;

    public StepJob(ExecutionLog log, IJobContext context)
    {
        _log = log;
        _context = context;
    }

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _log.Add($"done:{_context.JobId.Value}");
        return Task.CompletedTask;
    }
}
