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
/// TDD tests representing expected behaviors for items in TECH_DEBT_BACKLOG.md.
/// These tests are currently skipped because they require business logic changes.
/// </summary>
public sealed class KafkaFutureHardeningTests
{
    /// <summary>
    /// TD002: Kafka message without job_type header should be moved to DLT.
    /// Expected: Loop doesn't crash, message is moved to DLT topic.
    /// </summary>
    [Fact(Skip = "TD002: Kafka Missing Header Contract Breach. Requires moving header extraction inside try/catch.")]
    public async Task Kafka_MessageWithoutJobType_ShouldBeMovedToDeadLetter()
    {
        // Arrange
        var consumerMock = new Mock<IKafkaConsumer>();
        var schedulerMock = new Mock<IScheduler>();
        var options = new KafkaTriggerOptions
        {
            Topic = "test",
            DeadLetterTopic = "test-dlt",
            ConsumeTimeout = TimeSpan.FromMilliseconds(10),
        };

        var result = new ConsumeResult<string, string>
        {
            Message = new Message<string, string> { Key = "k1", Value = "{}", Headers = new Headers() },
            Topic = "test",
            Partition = 0,
            Offset = 1,
        };

        var sut = new KafkaTriggerHandler(
            Options.Create(options),
            consumerMock.Object,
            schedulerMock.Object,
            new NexJobOptions(),
            NullLogger<KafkaTriggerHandler>.Instance);

        // Act
        var method = typeof(KafkaTriggerHandler).GetMethod("ProcessMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(sut, new object[] { result, CancellationToken.None })!;

        // Assert
        consumerMock.Verify(x => x.ProduceToDeadLetterAsync("test-dlt", result, It.Is<Exception>(e => e is InvalidOperationException), It.IsAny<CancellationToken>()), Times.Once);
        consumerMock.Verify(x => x.Commit(result), Times.Once);
    }
}
