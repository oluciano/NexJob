using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace NexJob.MongoDB;

/// <summary>Serializes <see cref="Nullable{JobId}"/> for optional parent references.</summary>
internal sealed class NullableJobIdSerializer : SerializerBase<JobId?>
{
    public static readonly NullableJobIdSerializer Instance = new();

    public override JobId? Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        if (context.Reader.CurrentBsonType == BsonType.Null)
        {
            context.Reader.ReadNull();
            return null;
        }

        return new JobId(Guid.Parse(context.Reader.ReadString()));
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, JobId? value)
    {
        if (value is null)
            context.Writer.WriteNull();
        else
            context.Writer.WriteString(value.Value.Value.ToString());
    }
}
