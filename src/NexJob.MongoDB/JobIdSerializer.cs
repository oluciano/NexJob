using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace NexJob.MongoDB;

/// <summary>Serializes <see cref="JobId"/> as a BSON string (UUID format).</summary>
internal sealed class JobIdSerializer : SerializerBase<JobId>
{
    /// <summary>The singleton instance of the serializer.</summary>
    public static readonly JobIdSerializer Instance = new();

    /// <inheritdoc/>
    public override JobId Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var value = context.Reader.ReadString();
        return new JobId(Guid.Parse(value));
    }

    /// <inheritdoc/>
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, JobId value)
    {
        context.Writer.WriteString(value.Value.ToString());
    }
}
