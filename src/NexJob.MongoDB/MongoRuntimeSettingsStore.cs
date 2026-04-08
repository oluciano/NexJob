using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using NexJob.Configuration;

namespace NexJob.MongoDB;

/// <summary>
/// MongoDB-backed implementation of <see cref="IRuntimeSettingsStore"/>.
/// Persists runtime configuration as a JSON document in the <c>nexjob_settings</c>
/// collection so that dashboard overrides survive application restarts.
/// </summary>
public sealed class MongoRuntimeSettingsStore : IRuntimeSettingsStore
{
    private const string SettingsId = "runtime_settings";
    private readonly IMongoCollection<BsonDocument> _collection;

    /// <summary>Initializes a new <see cref="MongoRuntimeSettingsStore"/>.</summary>
    public MongoRuntimeSettingsStore(IMongoDatabase database)
    {
        _collection = database.GetCollection<BsonDocument>("nexjob_settings");
    }

    /// <inheritdoc/>
    public async Task<RuntimeSettings> GetAsync(CancellationToken ct = default)
    {
        var doc = await _collection
            .Find(Builders<BsonDocument>.Filter.Eq("_id", SettingsId))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (doc is null)
        {
            return new RuntimeSettings();
        }

        var json = doc["value"].AsString;
        return JsonSerializer.Deserialize<RuntimeSettings>(json) ?? new RuntimeSettings();
    }

    /// <inheritdoc/>
    public async Task SaveAsync(RuntimeSettings settings, CancellationToken ct = default)
    {
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(settings);

        var filter = Builders<BsonDocument>.Filter.Eq("_id", SettingsId);
        var update = Builders<BsonDocument>.Update
            .Set("value", json)
            .Set("updated_at", settings.UpdatedAt);

        await _collection.UpdateOneAsync(
            filter,
            update,
            new UpdateOptions { IsUpsert = true, },
            ct).ConfigureAwait(false);
    }
}
