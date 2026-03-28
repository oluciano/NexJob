using System;
using Microsoft.Extensions.Logging;

namespace NexJob.Sample.WorkerService.Jobs;

/// <summary>
/// Specifies the input data for processing an order.
/// </summary>
public record ProcessOrderInput(Guid OrderId, decimal Amount);

/// <summary>
/// Represents a job that processes an order and reports progress across multiple steps.
/// </summary>
public sealed class ProcessOrderJob : IJob<ProcessOrderInput>
{
    private readonly ILogger<ProcessOrderJob> _logger;
    private readonly IJobContext _ctx;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessOrderJob"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="ctx">ctx.</param>
    public ProcessOrderJob(ILogger<ProcessOrderJob> logger, IJobContext ctx)
    {
        _logger = logger;
        _ctx = ctx;
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(ProcessOrderInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing order {OrderId} — attempt {Attempt}",
            input.OrderId, _ctx.Attempt);

        await _ctx.ReportProgressAsync(0, "Starting...", cancellationToken);
        await Task.Delay(500, cancellationToken);

        await _ctx.ReportProgressAsync(50, "Processing payment...", cancellationToken);
        await Task.Delay(500, cancellationToken);

        await _ctx.ReportProgressAsync(100, "Done.", cancellationToken);
    }
}
