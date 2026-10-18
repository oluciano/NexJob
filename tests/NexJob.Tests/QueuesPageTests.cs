using FluentAssertions;
using Xunit;

namespace NexJob.Tests;

public sealed class QueuesPageTests
{
    [Fact]
    public async Task IdleQueueWithActiveWorker_IsListedWithZeroJobs()
    {
        // N1 (Positive): a queue with no Enqueued/Processing job but an active worker is still listed (#554).
        var html = await GetQueuesPageAsync("tracker-idle");

        html.Should().Contain("tracker-idle");
        html.Should().NotContain("No active queues.");
    }

    [Fact]
    public async Task QueueNoOneServes_IsNotListed()
    {
        // N2 (Negative): a queue with no jobs and no worker stays out of the page.
        var html = await GetQueuesPageAsync("tracker-idle");

        html.Should().NotContain("ghost-queue");
    }

    [Fact]
    public async Task WorkerQueueName_IsHtmlEncoded()
    {
        // N3 (Invalid Input): a hostile queue name from a heartbeat cannot inject markup.
        var html = await GetQueuesPageAsync("<script>alert(1)</script>");

        html.Should().NotContain("<script>alert(1)</script>");
    }

    private static Task<string> GetQueuesPageAsync(string workerQueue) =>
        DashboardTestHost.GetAsync("/dashboard/queues", storage => storage.RegisterServerAsync(new ServerRecord
        {
            Id = "queues-page-test-server",
            WorkerCount = 1,
            Queues = new[] { workerQueue },
            StartedAt = DateTimeOffset.UtcNow,
            HeartbeatAt = DateTimeOffset.UtcNow,
        }));

    [Theory]
    [InlineData("tracker-idle")]
    [InlineData("a'b\"c\\d<e>&f")]
    [InlineData("'")]
    [InlineData("\\")]
    public async Task PauseConfirmation_IsACompleteHandlerWithTheQueueName(string queue)
    {
        // N1 + N2 (#592): the Pause confirmation carries the queue name as stored, even with quotes and a backslash.
        var html = await GetQueuesPageAsync(queue);

        var confirms = InlineHandlers.Confirms(html);
        confirms.Should().NotContain(h => InlineHandlers.ConfirmMessage(h) == null);
        confirms.Select(h => InlineHandlers.ConfirmMessage(h)).Should().Contain($"Pause queue {queue}?");
    }
}
