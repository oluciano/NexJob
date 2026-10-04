using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Job control service scenarios on the InMemory storage (the default; no database or Docker needed).
/// </summary>
[Trait("Category", "Reliability.InMemory")]
public sealed class InMemoryControlServiceTests : ControlServiceScenarios
{
    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() => _ => { };
}
