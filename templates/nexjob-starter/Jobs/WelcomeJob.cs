using NexJob;

namespace NexJobStarter.Jobs;

/// <summary>Input for <see cref="WelcomeJob"/>.</summary>
public record WelcomeInput(string Name);

/// <summary>
/// A sample job that ships with the NexJob starter template.
/// Replace or delete this and add your own <see cref="IJob{TInput}"/> implementations.
/// </summary>
public sealed class WelcomeJob(ILogger<WelcomeJob> logger) : IJob<WelcomeInput>
{
    /// <inheritdoc/>
    public Task ExecuteAsync(WelcomeInput input, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Hello, {Name}! NexJob is working. Open /jobs to see the dashboard.",
            input.Name);
        return Task.CompletedTask;
    }
}
