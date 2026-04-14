using NexJob.Internal;
using NexJob.Storage;

namespace NexJob.IntegrationTests;

/// <summary>
/// Runs the full <see cref="StorageProviderTestsBase"/> contract against
/// the in-memory provider. No Docker required.
/// </summary>
public sealed class InMemoryStorageProviderTests : StorageProviderTestsBase
{
    protected override Task<(IJobStorage Job, IRecurringStorage Recurring, IDashboardStorage Dashboard, IStorageProvider Full)> CreateStorageAsync()
    {
        var provider = new InMemoryStorageProvider();
        return Task.FromResult<(IJobStorage, IRecurringStorage, IDashboardStorage, IStorageProvider)>((provider, provider, provider, provider));
    }
}
