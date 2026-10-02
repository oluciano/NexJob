using Microsoft.Extensions.Logging;

namespace NexJob.Sample.WorkerService.Jobs;

/// <summary>
/// Input for DeadLetterSampleJob.
/// </summary>
public record DeadLetterInput(string TransactionId, string Reason);

/// <summary>
/// A job that always fails to demonstrate exhaustion of attempts and dead-lettering.
/// </summary>
public sealed class DeadLetterSampleJob : IJob<DeadLetterInput>
{
    private readonly ILogger<DeadLetterSampleJob> _logger;
    private readonly IJobContext _ctx;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeadLetterSampleJob"/> class.
    /// </summary>
    public DeadLetterSampleJob(ILogger<DeadLetterSampleJob> logger, IJobContext ctx)
    {
        _logger = logger;
        _ctx = ctx;
    }

    /// <inheritdoc/>
    public Task ExecuteAsync(DeadLetterInput input, CancellationToken cancellationToken)
    {
        _logger.LogWarning("DeadLetterSampleJob {TxId} failing attempt {Attempt}", input.TransactionId, _ctx.Attempt);
        throw new InvalidOperationException($"Fraudulent transaction detected ({input.TransactionId}): {input.Reason}");
    }
}
