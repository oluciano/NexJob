using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Retry and dead-letter scenarios on the InMemory storage (the default; no database or Docker needed).
/// </summary>
[Trait("Category", "Reliability.InMemory")]
public sealed class InMemoryRetryAndDeadLetterTests : RetryAndDeadLetterScenarios
{
    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() => _ => { };
}
