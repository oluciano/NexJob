using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace NexJob.Tests;

public sealed class RecurringJobServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRecurringJob_NoInput_AddsToRecurringJobs()
    {
        var options = new NexJobOptions();

        var result = options.AddRecurringJob<NoInputJob>("job-1", "0 * * * *");

        result.Should().BeSameAs(options);
        options.RecurringJobs.Should().HaveCount(1);
        var job = options.RecurringJobs[0];
        job.Job.Should().Be(nameof(NoInputJob));
        job.ResolvedJobType.Should().Be(typeof(NoInputJob));
        job.Cron.Should().Be("0 * * * *");
        job.Queue.Should().Be("default");
        job.Enabled.Should().BeTrue();
        job.ConcurrencyPolicy.Should().Be(RecurringConcurrencyPolicy.SkipIfRunning);
    }

    [Fact]
    public void AddRecurringJob_WithInputJson_AddsWithSerializedJson()
    {
        var options = new NexJobOptions();
        var inputJson = "{\"value\":123}";

        options.AddRecurringJob<WithInputJob, InputModel>("job-2", "*/5 * * * *", inputJson, "queue-a");

        options.RecurringJobs.Should().HaveCount(1);
        var job = options.RecurringJobs[0];
        job.ResolvedInputJson.Should().Be(inputJson);
        job.Queue.Should().Be("queue-a");
        job.ResolvedJobType.Should().Be(typeof(WithInputJob));
    }

    [Fact]
    public void AddRecurringJob_WithInputObject_SerializesAndAdds()
    {
        var options = new NexJobOptions();
        var input = new InputModel(42, "abc");

        options.AddRecurringJob<WithInputJob, InputModel>("job-3", "*/10 * * * *", input, "queue-b");

        options.RecurringJobs.Should().HaveCount(1);
        var job = options.RecurringJobs[0];
        job.ResolvedInputJson.Should().Be(JsonSerializer.Serialize(input));
        job.Job.Should().Be(nameof(WithInputJob));
    }

    [Fact]
    public void AddRecurringJob_DefaultsAreCorrect()
    {
        var options = new NexJobOptions();

        options.AddRecurringJob<NoInputJob>("job-4", "0 0 * * *");

        var job = options.RecurringJobs[0];
        job.Id.Should().Be("job-4");
        job.Queue.Should().Be("default");
        job.TimeZoneId.Should().BeNull();
        job.Enabled.Should().BeTrue();
        job.ConcurrencyPolicy.Should().Be(RecurringConcurrencyPolicy.SkipIfRunning);
    }

    [Fact]
    public void AddRecurringJob_ReturnsOptionForChaining()
    {
        var options = new NexJobOptions();

        var result = options.AddRecurringJob<NoInputJob>("job-5", "* * * * *");

        result.Should().BeSameAs(options);
    }

    public sealed class NoInputJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class WithInputJob : IJob<InputModel>
    {
        public Task ExecuteAsync(InputModel input, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed record InputModel(int Value, string Name);
}
