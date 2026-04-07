using FluentAssertions;
using NexJob.Exceptions;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

public sealed class IdempotencyPolicyTests
{
    private readonly InMemoryStorageProvider _storage = new();

    private static JobRecord MakeJob(JobStatus status = JobStatus.Enqueued, string? idempotencyKey = null) =>
        new()
        {
            Id = JobId.New(),
            JobType = typeof(StubJob).AssemblyQualifiedName!,
            InputType = typeof(string).AssemblyQualifiedName!,
            InputJson = "\"test\"",
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = status,
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = 10,
        };

    // 1. AllowAfterFailed — job em Failed → aceita novo
    [Fact]
    public async Task EnqueueAsync_AllowAfterFailed_WhenPreviousFailed_EnqueuesNewJob()
    {
        var job1 = MakeJob(status: JobStatus.Failed, idempotencyKey: "key-1");
        await _storage.EnqueueAsync(job1, DuplicatePolicy.AllowAfterFailed);

        var job2 = MakeJob(idempotencyKey: "key-1");
        var result = await _storage.EnqueueAsync(job2, DuplicatePolicy.AllowAfterFailed);

        result.WasRejected.Should().BeFalse();
        result.JobId.Should().Be(job2.Id);
    }

    // 2. RejectIfFailed — job em Failed → rejeita
    [Fact]
    public async Task EnqueueAsync_RejectIfFailed_WhenPreviousFailed_Throws()
    {
        var job1 = MakeJob(status: JobStatus.Failed, idempotencyKey: "key-2");
        await _storage.EnqueueAsync(job1, DuplicatePolicy.AllowAfterFailed);

        var job2 = MakeJob(idempotencyKey: "key-2");
        var result = await _storage.EnqueueAsync(job2, DuplicatePolicy.RejectIfFailed);

        result.WasRejected.Should().BeTrue();
        result.JobId.Should().Be(job1.Id);
    }

    // 3. RejectAlways — job em Succeeded → rejeita
    [Fact]
    public async Task EnqueueAsync_RejectAlways_WhenPreviousSucceeded_Throws()
    {
        var job1 = MakeJob(status: JobStatus.Succeeded, idempotencyKey: "key-3");
        await _storage.EnqueueAsync(job1, DuplicatePolicy.AllowAfterFailed);

        var job2 = MakeJob(idempotencyKey: "key-3");
        var result = await _storage.EnqueueAsync(job2, DuplicatePolicy.RejectAlways);

        result.WasRejected.Should().BeTrue();
        result.JobId.Should().Be(job1.Id);
    }

    // 4. RejectAlways — job em Failed → rejeita
    [Fact]
    public async Task EnqueueAsync_RejectAlways_WhenPreviousFailed_Throws()
    {
        var job1 = MakeJob(status: JobStatus.Failed, idempotencyKey: "key-4");
        await _storage.EnqueueAsync(job1, DuplicatePolicy.AllowAfterFailed);

        var job2 = MakeJob(idempotencyKey: "key-4");
        var result = await _storage.EnqueueAsync(job2, DuplicatePolicy.RejectAlways);

        result.WasRejected.Should().BeTrue();
        result.JobId.Should().Be(job1.Id);
    }

    // 5. AllowAfterFailed — job em Succeeded → aceita novo
    [Fact]
    public async Task EnqueueAsync_AllowAfterFailed_WhenPreviousSucceeded_EnqueuesNewJob()
    {
        var job1 = MakeJob(status: JobStatus.Succeeded, idempotencyKey: "key-5");
        await _storage.EnqueueAsync(job1, DuplicatePolicy.AllowAfterFailed);

        var job2 = MakeJob(idempotencyKey: "key-5");
        var result = await _storage.EnqueueAsync(job2, DuplicatePolicy.AllowAfterFailed);

        result.WasRejected.Should().BeFalse();
        result.JobId.Should().Be(job2.Id);
    }

    // 6. RejectIfFailed — job em Succeeded → aceita novo
    [Fact]
    public async Task EnqueueAsync_RejectIfFailed_WhenPreviousSucceeded_EnqueuesNewJob()
    {
        var job1 = MakeJob(status: JobStatus.Succeeded, idempotencyKey: "key-6");
        await _storage.EnqueueAsync(job1, DuplicatePolicy.AllowAfterFailed);

        var job2 = MakeJob(idempotencyKey: "key-6");
        var result = await _storage.EnqueueAsync(job2, DuplicatePolicy.RejectIfFailed);

        result.WasRejected.Should().BeFalse();
        result.JobId.Should().Be(job2.Id);
    }

    // 7. Qualquer policy — job em Processing → deduplica sem rejeição
    [Fact]
    public async Task EnqueueAsync_AnyPolicy_WhenProcessing_DeduplicatesWithoutRejection()
    {
        var job1 = MakeJob(status: JobStatus.Processing, idempotencyKey: "key-7");
        await _storage.EnqueueAsync(job1, DuplicatePolicy.AllowAfterFailed);

        var job2 = MakeJob(idempotencyKey: "key-7");
        var result = await _storage.EnqueueAsync(job2, DuplicatePolicy.RejectAlways);

        result.WasRejected.Should().BeFalse();
        result.JobId.Should().Be(job1.Id);
    }

    // 8. DuplicateJobException — contém idempotencyKey, existingJobId e policy corretos
    [Fact]
    public async Task EnqueueAsync_WhenRejected_ExceptionContainsCorrectDetails()
    {
        var job1 = MakeJob(status: JobStatus.Failed, idempotencyKey: "key-8");
        await _storage.EnqueueAsync(job1, DuplicatePolicy.AllowAfterFailed);

        var job2 = MakeJob(idempotencyKey: "key-8");
        var result = await _storage.EnqueueAsync(job2, DuplicatePolicy.RejectIfFailed);

        result.WasRejected.Should().BeTrue();
        result.JobId.Should().Be(job1.Id);

        var ex = new DuplicateJobException("key-8", result.JobId, DuplicatePolicy.RejectIfFailed);
        ex.IdempotencyKey.Should().Be("key-8");
        ex.ExistingJobId.Should().Be(job1.Id);
        ex.Policy.Should().Be(DuplicatePolicy.RejectIfFailed);
        ex.Message.Should().Contain("key-8");
        ex.Message.Should().Contain(job1.Id.Value.ToString());
        ex.Message.Should().Contain("RejectIfFailed");
    }
}
