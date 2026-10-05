using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NexJob;

namespace NexJob.Sample.WorkerService.Jobs;

/// <summary>
/// Input for FlakyApiJob.
/// </summary>
public record FlakyApiInput(string ActionName, int FailUntilAttempt);

/// <summary>
/// A job that intentionally fails during its first attempts to demonstrate the retry loop,
/// backoff, and eventual recovery or dead-letter in the dashboard.
/// </summary>
[Retry(3, InitialDelay = "00:00:08", Multiplier = 2.0)]
public sealed class FlakyApiJob : IJob<FlakyApiInput>
{
    private static readonly ConcurrentDictionary<string, int> _attemptsPerAction = new(StringComparer.Ordinal);
    private readonly ILogger<FlakyApiJob> _logger;
    private readonly IJobContext _ctx;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlakyApiJob"/> class.
    /// </summary>
    public FlakyApiJob(ILogger<FlakyApiJob> logger, IJobContext ctx)
    {
        _logger = logger;
        _ctx = ctx;
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(FlakyApiInput input, CancellationToken cancellationToken)
    {
        var currentAttempt = _attemptsPerAction.AddOrUpdate(input.ActionName, 1, (_, count) => count + 1);

        _logger.LogInformation("FlakyApiJob '{Action}' running attempt {Attempt} (target fail until {FailUntil})",
            input.ActionName, currentAttempt, input.FailUntilAttempt);

        await _ctx.ReportProgressAsync(25, $"Contacting external provider (Attempt {currentAttempt})...", cancellationToken);
        await Task.Delay(400, cancellationToken);

        if (currentAttempt <= input.FailUntilAttempt)
        {
            await _ctx.ReportProgressAsync(50, "Connection refused / Gateway timeout", cancellationToken);
            throw new HttpRequestException($"Gateway 504 Gateway Timeout while syncing '{input.ActionName}' (Simulated transient failure on attempt {currentAttempt})");
        }

        await _ctx.ReportProgressAsync(100, "Successfully recovered and processed!", cancellationToken);
        _logger.LogInformation("FlakyApiJob '{Action}' SUCCEEDED on attempt {Attempt}!", input.ActionName, currentAttempt);
    }
}
