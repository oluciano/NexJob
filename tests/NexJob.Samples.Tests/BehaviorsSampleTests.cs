using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NexJob.Sample.Reliability;
using Xunit;

namespace NexJob.Samples.Tests;

/// <summary>The Reliability sample does what its README says, on in-memory storage (issue #288).</summary>
public sealed class BehaviorsSampleTests : IClassFixture<WebApplicationFactory<FlakyJob>>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient _client;

    public BehaviorsSampleTests(WebApplicationFactory<FlakyJob> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_IsHealthy()
    {
        // N1
        (await _client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FlakyJob_FailsTwice_ThenSucceedsOnTheThirdAttempt()
    {
        // N1
        var id = await PostAsync("/retry/flaky");

        var job = await WaitForStatusAsync(id, "Succeeded");

        job.GetProperty("attempts").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task CheckpointJob_AfterTheCrash_ResumesWithoutRepeatingFinishedSteps()
    {
        // N1
        var id = await PostAsync("/checkpoint");

        await WaitForStatusAsync(id, "Succeeded");

        var steps = (await _client.GetFromJsonAsync<string[]>("/log"))!
            .Where(e => e.StartsWith($"checkpoint:{id}:", StringComparison.Ordinal))
            .Select(e => e[(e.LastIndexOf(':') + 1)..])
            .ToArray();
        steps.Should().Equal("step-1", "step-2", "step-3", "step-4", "step-5");
    }

    [Fact]
    public async Task DeadLetterDemo_ExhaustsItsAttempts_AndCallsTheHandler()
    {
        // N2
        var id = await PostAsync("/deadletter");

        var job = await WaitForStatusAsync(id, "Failed");

        job.GetProperty("attempts").GetInt32().Should().Be(2);
        await WaitUntilAsync(async () => Array.Exists((await _client.GetFromJsonAsync<string[]>("/log"))!, e => e.StartsWith($"deadletter:{id}:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task DeadlineDemo_JobWaitsPastItsDeadlineInAPausedQueue_AndExpires()
    {
        // N2
        var id = await PostAsync("/deadline/demo");
        await Task.Delay(TimeSpan.FromSeconds(3)); // longer than the 2 second deadline

        (await _client.PostAsync("/queues/held/resume", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await WaitForStatusAsync(id, "Expired");
        (await _client.GetFromJsonAsync<string[]>("/log"))!.Should().NotContain(e => e == $"quick:{id}", "an expired job never runs");
    }

    [Fact]
    public async Task CircuitDemo_ThreeFailuresOpenTheCircuit_AndResetLetsTheProbeRun()
    {
        // N2
        var trip = await _client.PostAsync("/circuit/trip", null);
        var failing = (await trip.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobIds").EnumerateArray().Select(e => e.GetGuid()).ToArray();
        foreach (var failedId in failing)
        {
            await WaitForStatusAsync(failedId, "Failed");
        }

        var probe = await PostAsync("/circuit/probe");
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        (await GetJobAsync(probe)).GetProperty("status").GetString().Should().Be("Enqueued", "the circuit is open");

        (await _client.PostAsync("/queues/fragile/reset-circuit", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await WaitForStatusAsync(probe, "Succeeded");
    }

    [Fact]
    public async Task UnknownJob_Returns404()
    {
        // N3
        (await _client.GetAsync($"/jobs/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteJob_RemovesIt()
    {
        // N3 (control service): a finished job can be deleted and is then gone.
        var id = await PostAsync("/retry/flaky");
        await WaitForStatusAsync(id, "Succeeded");

        (await _client.DeleteAsync($"/jobs/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _client.GetAsync($"/jobs/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<Guid> PostAsync(string path)
    {
        var response = await _client.PostAsync(path, null);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();
    }

    private async Task<JsonElement> GetJobAsync(Guid id) => await _client.GetFromJsonAsync<JsonElement>($"/jobs/{id}");

    private async Task<JsonElement> WaitForStatusAsync(Guid id, string status)
    {
        JsonElement last = default;
        await WaitUntilAsync(async () =>
        {
            last = await GetJobAsync(id);
            return last.GetProperty("status").GetString() == status;
        });
        return last;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not reached in time.");
            }

            await Task.Delay(100);
        }
    }
}
