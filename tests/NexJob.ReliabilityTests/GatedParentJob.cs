using Microsoft.Extensions.Logging;

namespace NexJob.ReliabilityTests;

/// <summary>
/// A parent job that records when it starts and finishes and waits for the test to decide whether it succeeds.
/// </summary>
internal sealed class GatedParentJob : IJob
{
    private readonly ExecutionLog _log;
    private readonly ParentGate _gate;
    private readonly IJobContext _context;
    private readonly ILogger<GatedParentJob> _logger;

    public GatedParentJob(ExecutionLog log, ParentGate gate, IJobContext context, ILogger<GatedParentJob> logger)
    {
        _log = log;
        _gate = gate;
        _context = context;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GatedParentJob started");
        _log.Add($"start:{_context.JobId.Value}");

        var succeed = await _gate.Opened.WaitAsync(cancellationToken);
        if (!succeed)
        {
            throw new InvalidOperationException("Parent fails intentionally");
        }

        _log.Add($"done:{_context.JobId.Value}");
    }
}
