using NexJob;

namespace NexJob.Sample.WebApi.Jobs;

public sealed record BulkEmailPayload(string[] Recipients, string Subject, string Body);

public sealed class BulkEmailJob(ILogger<BulkEmailJob> logger) : IJob<BulkEmailPayload>
{
    public async Task ExecuteAsync(BulkEmailPayload input, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[BulkEmailJob] Starting bulk send to {Count} recipients — Subject: {Subject}",
            input.Recipients.Length, input.Subject);

        foreach (var recipient in input.Recipients)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogInformation("[BulkEmailJob] Sending to {Recipient}", recipient);
            await Task.Delay(100, cancellationToken); // simulate SMTP per recipient
        }

        logger.LogInformation("[BulkEmailJob] Bulk send complete ({Count} emails)", input.Recipients.Length);
    }
}
