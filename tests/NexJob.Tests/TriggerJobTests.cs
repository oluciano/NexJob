using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// What every broker trigger decides the same way: how a received message becomes a job, and which failures are permanent.
/// </summary>
public sealed class TriggerJobTests
{
    private readonly NexJobOptions _options = new();

    // ─── N1: positive ──────────────────────────────────────────────────────

    /// <summary>N1: a message becomes an enqueued job on the target queue, tagged with its broker, carrying the key and the trace context.</summary>
    [Fact]
    public void Build_FromAMessage_ProducesTheJobTheTriggersAlwaysBuilt()
    {
        var job = TriggerJob.Build("MyJob", "payload", "msg-1", "kafka", "00-trace-01", _options, "emails", JobPriority.High);

        job.JobType.Should().Be("MyJob");
        job.Status.Should().Be(JobStatus.Enqueued);
        job.Queue.Should().Be("emails");
        job.Priority.Should().Be(JobPriority.High);
        job.IdempotencyKey.Should().Be("msg-1");
        job.TraceParent.Should().Be("00-trace-01");
        job.Tags.Should().Equal("trigger:kafka");
        job.InputType.Should().Be(typeof(string).AssemblyQualifiedName);
    }

    /// <summary>N1: the body is stored as a JSON string, so the job receives it verbatim whatever it contains.</summary>
    /// <param name="body">The message body.</param>
    [Theory]
    [InlineData("plain text")]
    [InlineData("{\"a\":1}")]
    [InlineData("quote \" and \\ backslash")]
    public void Build_StoresTheBodyAsAJsonString(string body)
    {
        var job = TriggerJob.Build("MyJob", body, "k", "sqs", null, _options, "default", JobPriority.Normal);

        JsonSerializer.Deserialize<string>(job.InputJson).Should().Be(body);
    }

    /// <summary>N1: a malformed value, malformed JSON and an invalid argument (and its subtypes) can never succeed on redelivery.</summary>
    /// <param name="type">The exception type.</param>
    [Theory]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(JsonException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(ArgumentNullException))]
    public void IsPermanentFailure_ForAnUnusableMessage_IsTrue(Type type)
        => TriggerJob.IsPermanentFailure((Exception)Activator.CreateInstance(type)!).Should().BeTrue();

    // ─── N2: negative ──────────────────────────────────────────────────────

    /// <summary>N2: storage, network and timeout failures are transient: the message is given back, never dead-lettered.</summary>
    /// <param name="type">The exception type.</param>
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(OperationCanceledException))]
    public void IsPermanentFailure_ForATransientFailure_IsFalse(Type type)
        => TriggerJob.IsPermanentFailure((Exception)Activator.CreateInstance(type)!).Should().BeFalse();

    // ─── N3: invalid / boundary ────────────────────────────────────────────

    /// <summary>N3: a broker that gives no message id yields no idempotency key, and an empty body is a valid empty JSON string.</summary>
    [Fact]
    public void Build_WithoutKeyAndWithEmptyBody_KeepsBothEmpty()
    {
        var job = TriggerJob.Build("MyJob", string.Empty, null, "rabbitmq", null, _options, "default", JobPriority.Normal);

        job.IdempotencyKey.Should().BeNull();
        JsonSerializer.Deserialize<string>(job.InputJson).Should().BeEmpty();
    }
}
