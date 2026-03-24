using NexJob.Internal;
using NexJob.Storage;

namespace NexJob.IntegrationTests;

/// <summary>
/// Runs the full <see cref="StorageProviderTestsBase"/> contract against
/// the in-memory provider. No Docker required.
/// </summary>
public sealed class InMemoryStorageProviderTests : StorageProviderTestsBase
{
    protected override Task<IStorageProvider> CreateStorageAsync() =>
        Task.FromResult<IStorageProvider>(new InMemoryStorageProvider());
}
