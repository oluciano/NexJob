using NexJob;

namespace NexJob.Sample.WebApi.Jobs;

public sealed record CleanupRequest(string Target, int RetentionDays);

public sealed class CleanupJob(ILogger<CleanupJob> logger) : IJob<CleanupRequest>
{
    public async Task ExecuteAsync(CleanupRequest input, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[CleanupJob] Cleaning '{Target}' — retaining last {Days} days",
            input.Target, input.RetentionDays);

        await Task.Delay(300, cancellationToken);

        logger.LogInformation("[CleanupJob] '{Target}' cleanup done.", input.Target);
    }
}
