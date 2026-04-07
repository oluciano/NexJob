using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace NexJob.MongoDB;

/// <summary>Serializes nullable <see cref="JobId"/> as a BSON string (UUID format) or Null.</summary>
internal sealed class NullableJobIdSerializer : SerializerBase<JobId?>
{
    /// <summary>The singleton instance of the serializer.</summary>
    public static readonly NullableJobIdSerializer Instance = new();

    /// <inheritdoc/>
    public override JobId? Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        if (context.Reader.CurrentBsonType == BsonType.Null)
        {
            context.Reader.ReadNull();
            return null;
        }

        var value = context.Reader.ReadString();
        return new JobId(Guid.Parse(value));
    }

    /// <inheritdoc/>
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, JobId? value)
    {
        if (value == null)
        {
            context.Writer.WriteNull();
        }
        else
        {
            context.Writer.WriteString(value.Value.Value.ToString());
        }
    }
}
