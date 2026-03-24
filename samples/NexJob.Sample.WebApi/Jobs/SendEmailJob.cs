using NexJob;

namespace NexJob.Sample.WebApi.Jobs;

public sealed record EmailPayload(string To, string Subject, string Body);

public sealed class SendEmailJob(ILogger<SendEmailJob> logger) : IJob<EmailPayload>
{
    public async Task ExecuteAsync(EmailPayload input, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[SendEmailJob] Sending email to {To} — Subject: {Subject}",
            input.To, input.Subject);

        await Task.Delay(500, cancellationToken); // simulate SMTP

        logger.LogInformation("[SendEmailJob] Email sent to {To}", input.To);
    }
}
