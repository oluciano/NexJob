using System.Text.Json;

namespace NexJob.Internal;

/// <summary>
/// What every broker trigger decides the same way: how a received message becomes a <see cref="JobRecord"/>, and which
/// failures can never succeed on redelivery. The triggers keep only what is theirs: reading the broker's headers and
/// the acknowledge, nack and dead-letter calls.
/// </summary>
internal static class TriggerJob
{
    /// <summary>Gets whether a failure is permanent: the message itself is unusable, so redelivering it can never help.</summary>
    /// <param name="exception">The failure raised while reading or enqueuing the message.</param>
    /// <returns><c>true</c> for a malformed value, malformed JSON or an invalid argument.</returns>
    public static bool IsPermanentFailure(Exception exception) =>
        exception is FormatException or JsonException or ArgumentException;

    /// <summary>
    /// Builds the job for a received message. The body is stored as a JSON string, so the job receives it verbatim and
    /// deserializing it to a concrete type is the job's responsibility; the idempotency key makes redelivery harmless.
    /// </summary>
    /// <param name="jobType">The job type to run.</param>
    /// <param name="body">The message body, as text.</param>
    /// <param name="idempotencyKey">The key that identifies this message across redeliveries; <c>null</c> when the broker gives none, so every delivery is a new job.</param>
    /// <param name="source">The broker name, tagged on the job as <c>trigger:{source}</c>.</param>
    /// <param name="traceParent">The W3C trace context carried by the message, if any.</param>
    /// <param name="options">The NexJob options.</param>
    /// <param name="queue">The target queue.</param>
    /// <param name="priority">The job priority.</param>
    /// <returns>The job, ready to enqueue.</returns>
    public static JobRecord Build(
        string jobType,
        string body,
        string? idempotencyKey,
        string source,
        string? traceParent,
        NexJobOptions options,
        string queue,
        JobPriority priority) =>
        JobRecordFactory.Build(
            jobType: jobType,
            inputType: typeof(string).AssemblyQualifiedName!,
            inputJson: JsonSerializer.Serialize(body),
            options: options,
            queue: queue,
            priority: priority,
            idempotencyKey: idempotencyKey,
            status: JobStatus.Enqueued,
            scheduledAt: null,
            tags: new[] { $"trigger:{source}" },
            expiresAt: null,
            traceParent: traceParent);
}
