using NexJob;

namespace NexJob.Sample.Providers;

/// <summary>The input of <see cref="EchoJob"/>.</summary>
/// <param name="Message">The text to write to the log.</param>
public sealed record EchoInput(string Message);

/// <summary>Writes its message to the log. The same job runs on every provider.</summary>
public sealed class EchoJob : IJob<EchoInput>
{
    private readonly ILogger<EchoJob> _logger;

    /// <summary>Initializes a new instance of the <see cref="EchoJob"/> class.</summary>
    /// <param name="logger">The logger.</param>
    public EchoJob(ILogger<EchoJob> logger) => _logger = logger;

    /// <inheritdoc/>
    public Task ExecuteAsync(EchoInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Echo: {Message}", input.Message);
        return Task.CompletedTask;
    }
}
