using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace NexJob.MongoDB;

/// <summary>
/// Serializes <see cref="DateTimeOffset"/> members of the NexJob documents as ISO 8601 strings, without touching
/// the serializer registry that the host application shares.
/// </summary>
internal sealed class DateTimeOffsetAsStringConvention : ConventionBase, IMemberMapConvention
{
    /// <inheritdoc/>
    public void Apply(BsonMemberMap memberMap)
    {
        if (memberMap.MemberType == typeof(DateTimeOffset))
        {
            memberMap.SetSerializer(new DateTimeOffsetSerializer(BsonType.String));
        }
        else if (memberMap.MemberType == typeof(DateTimeOffset?))
        {
            memberMap.SetSerializer(new NullableSerializer<DateTimeOffset>(new DateTimeOffsetSerializer(BsonType.String)));
        }
    }
}
