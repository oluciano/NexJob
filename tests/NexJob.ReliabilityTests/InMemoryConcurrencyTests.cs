using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Concurrency scenarios on the InMemory storage (the default; no database or Docker needed).
/// </summary>
[Trait("Category", "Reliability.InMemory")]
public sealed class InMemoryConcurrencyTests : ConcurrencyScenarios
{
    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() => _ => { };
}
