using Xunit;

namespace NexJob.IntegrationTests;

[CollectionDefinition("Redis")]
public class RedisCollection : ICollectionFixture<RedisFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
