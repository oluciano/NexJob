namespace NexJob.ReliabilityTests;

/// <summary>
/// A short job that records its own id and the node it ran on. The small delay keeps one node from finishing everything
/// before the others get to poll.
/// </summary>
internal sealed class NodeStepJob : IJob
{
    private readonly ExecutionLog _log;
    private readonly IJobContext _context;
    private readonly NodeName _node;

    public NodeStepJob(ExecutionLog log, IJobContext context, NodeName node)
    {
        _log = log;
        _context = context;
        _node = node;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken);
        _log.Add($"done:{_context.JobId.Value}@{_node.Value}");
    }
}
