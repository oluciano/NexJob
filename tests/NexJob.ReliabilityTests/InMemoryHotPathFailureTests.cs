using Microsoft.Extensions.DependencyInjection;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// The storage failing at the hot-path call sites, on the InMemory storage.
/// </summary>
[Trait("Category", "Reliability.InMemory")]
public sealed class InMemoryHotPathFailureTests : HotPathFailureScenarios
{
    /// <inheritdoc/>
    protected override Action<IServiceCollection> Storage() =>
        s =>
        {
            // The same registrations AddNexJob makes by default, done first so the fault proxy has something to wrap.
            s.AddSingleton<InMemoryStorageProvider>();
            s.AddSingleton<IStorageProvider>(sp => sp.GetRequiredService<InMemoryStorageProvider>());
            s.AddSingleton<IJobStorage>(sp => sp.GetRequiredService<InMemoryStorageProvider>());
            s.AddSingleton<IRecurringStorage>(sp => sp.GetRequiredService<InMemoryStorageProvider>());
            s.AddSingleton<IDashboardStorage>(sp => sp.GetRequiredService<InMemoryStorageProvider>());
        };
}
