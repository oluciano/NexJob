using MongoDB.Driver;

namespace NexJob.IntegrationTests;

/// <summary>
/// Creates uniquely named MongoDB test databases and drops them when disposed.
/// Every Mongo test creates its own database; without cleanup the files held open by the mongod container grow with
/// the number of tests until the server aborts with "Too many open files" (issue #248).
/// </summary>
public sealed class MongoTestDatabases : IAsyncDisposable
{
    private readonly List<(IMongoClient Client, string Name)> _created = [];

    /// <summary>Creates a client and a database with a unique name; the database is dropped by <see cref="DisposeAsync"/>.</summary>
    /// <param name="connectionString">Connection string of the test container.</param>
    /// <param name="prefix">Prefix that makes the database recognizable in the server.</param>
    /// <returns>The new, empty database.</returns>
    public IMongoDatabase Create(string connectionString, string prefix)
    {
        var client = new MongoClient(connectionString);
        var name = $"{prefix}_{Guid.NewGuid():N}";
        _created.Add((client, name));
        return client.GetDatabase(name);
    }

    /// <summary>Drops every database created so far.</summary>
    /// <returns>A task that completes when the cleanup finished.</returns>
    public async ValueTask DisposeAsync()
    {
        foreach (var (client, name) in _created)
        {
            try
            {
                await client.DropDatabaseAsync(name);
            }
            catch (MongoException)
            {
                // Best effort: a failed cleanup must not hide the outcome of the test itself.
            }

            // The client's cluster is not disposed: the 2.x driver shares it between clients with equal settings,
            // so closing it would break every other test.
        }

        _created.Clear();
    }
}
