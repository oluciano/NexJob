using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NexJob.Internal;
using Xunit;

namespace NexJob.Trigger.Kafka.Tests;

/// <summary>
/// Hardening unit tests for <see cref="KafkaTriggerHandler"/>.
/// Targets 100% branch coverage for Kafka topic polling and dead-letter logic.
/// </summary>
public sealed class KafkaTriggerHardeningTests
{
    private readonly Mock<IKafkaConsumer> _consumerMock = new();
    private readonly Mock<IScheduler> _schedulerMock = new();
    private readonly KafkaTriggerOptions _options = new()
    {
        Topic = "test-topic",
        ConsumeTimeout = TimeSpan.FromMilliseconds(10),
    };
    private readonly NexJobOptions _nexJobOptions = new();

    private KafkaTriggerHandler CreateSut()
    {
        return new KafkaTriggerHandler(
            Options.Create(_options),
            _consumerMock.Object,
            _schedulerMock.Object,
            _nexJobOptions,
            NullLogger<KafkaTriggerHandler>.Instance);
    }

    private ConsumeResult<string, string> CreateResult(string key, string? jobType = "MyJob")
    {
        var headers = new Headers();
        if (jobType != null)
        {
            headers.Add("nexjob.job_type", Encoding.UTF8.GetBytes(jobType));
        }

        return new ConsumeResult<string, string>
        {
            Message = new Message<string, string> { Key = key, Value = "{}", Headers = headers },
            Topic = "test-topic",
            Partition = 0,
            Offset = 1,
        };
    }

    // ─── Polling Loop Branches ─────────────────────────────────────────────

    /// <summary>Tests that polling loop handles null results and EOF correctly.</summary>
    [Fact]
    public async Task ExecuteAsync_HandlesNullAndEOF_ContinuesLoop()
    {
        _consumerMock.SetupSequence(x => x.Consume(It.IsAny<TimeSpan>()))
            .Returns((ConsumeResult<string, string>?)null)
            .Returns(new ConsumeResult<string, string> { IsPartitionEOF = true })
            .Throws(new OperationCanceledException()); // Way to stop BackgroundService

        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await sut.StopAsync(CancellationToken.None);

        _consumerMock.Verify(x => x.Consume(It.IsAny<TimeSpan>()), Times.AtLeast(2));
    }

    // ─── Error Handling & DLT Branches ─────────────────────────────────────

    /// <summary>Tests that enqueue failure with DLT configured moves message to DLT and commits.</summary>
    [Fact]
    public async Task ProcessMessageAsync_EnqueueFails_WithDLT_MovesToDLTAndCommits()
    {
        _options.DeadLetterTopic = "test-dlt";
        var sut = CreateSut();
        var result = CreateResult("k1");

        _schedulerMock.Setup(x => x.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Enqueue failed"));

        // Use reflection to call the private ProcessMessageAsync
        var method = typeof(KafkaTriggerHandler).GetMethod("ProcessMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(sut, new object[] { result, CancellationToken.None })!;

        _consumerMock.Verify(x => x.ProduceToDeadLetterAsync("test-dlt", result, It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Once);
        _consumerMock.Verify(x => x.Commit(result), Times.Once);
    }

    /// <summary>Tests that enqueue failure without DLT does NOT commit (allows retry on restart).</summary>
    [Fact]
    public async Task ProcessMessageAsync_EnqueueFails_NoDLT_DoesNotCommit()
    {
        _options.DeadLetterTopic = null;
        var sut = CreateSut();
        var result = CreateResult("k1");

        _schedulerMock.Setup(x => x.EnqueueAsync(It.IsAny<JobRecord>(), It.IsAny<DuplicatePolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Enqueue failed"));

        var method = typeof(KafkaTriggerHandler).GetMethod("ProcessMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(sut, new object[] { result, CancellationToken.None })!;

        _consumerMock.Verify(x => x.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
    }

    /// <summary>Tests that missing job_type header throws an exception.</summary>
    [Fact]
    public async Task ProcessMessageAsync_MissingJobType_Throws()
    {
        var sut = CreateSut();
        var result = CreateResult("k1", jobType: null);

        var method = typeof(KafkaTriggerHandler).GetMethod("ProcessMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Func<Task> act = () => (Task)method!.Invoke(sut, new object[] { result, CancellationToken.None })!;

        // When using MethodInfo.Invoke, the exception is usually wrapped in TargetInvocationException
        await act.Should().ThrowAsync<Exception>();
    }
}
