using MongoDB.Bson.Serialization.Attributes;

namespace NexJob.MongoDB;

/// <summary>
/// MongoDB document representing an active worker node/server.
/// </summary>
internal sealed class ServerDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("worker_count")]
    public int WorkerCount { get; set; }

    [BsonElement("queues")]
    public IReadOnlyList<string> Queues { get; set; } = Array.Empty<string>();

    [BsonElement("started_at")]
    public DateTimeOffset StartedAt { get; set; }

    [BsonElement("heartbeat_at")]
    public DateTimeOffset HeartbeatAt { get; set; }

    public static ServerDocument FromRecord(ServerRecord record) => new()
    {
        Id = record.Id,
        WorkerCount = record.WorkerCount,
        Queues = record.Queues,
        StartedAt = record.StartedAt,
        HeartbeatAt = record.HeartbeatAt,
    };

    public ServerRecord ToRecord() => new()
    {
        Id = Id,
        WorkerCount = WorkerCount,
        Queues = Queues,
        StartedAt = StartedAt,
        HeartbeatAt = HeartbeatAt,
    };
}
