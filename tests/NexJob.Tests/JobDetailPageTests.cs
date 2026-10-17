using FluentAssertions;
using Xunit;

namespace NexJob.Tests;

public sealed class JobDetailPageTests
{
    private const string Hostile = "a'b\"c\\d<e>&f";

    private static JobRecord Job(JobStatus status, string? idempotencyKey = null, string? traceParent = null) => new()
    {
        Id = JobId.New(),
        JobType = "Acme.Jobs.ReportJob",
        Queue = "default",
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
        ScheduledAt = status == JobStatus.Scheduled ? DateTimeOffset.UtcNow.AddHours(1) : null,
        IdempotencyKey = idempotencyKey,
        TraceParent = traceParent,
    };

    [Theory]
    [InlineData(JobStatus.Failed, "Requeue this job?")]
    [InlineData(JobStatus.Scheduled, "Run this job now (bypass schedule)?")]
    public async Task ActionConfirmations_AreCompleteHandlers(JobStatus status, string expected)
    {
        // N1 (#592): Requeue and Run Now ask for confirmation with complete handlers.
        var job = Job(status);
        var html = await DashboardTestHost.GetAsync($"/dashboard/jobs/{job.Id.Value}", s => s.EnqueueAsync(job));

        var confirms = InlineHandlers.Confirms(html);
        confirms.Should().NotContain(h => InlineHandlers.ConfirmMessage(h) == null);
        confirms.Select(h => InlineHandlers.ConfirmMessage(h)).Should().Contain(m => m != null && m.StartsWith(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("order-42")]
    [InlineData(Hostile)]
    public async Task CopyIdempotencyKey_CopiesTheStoredKey(string key)
    {
        // N1 + N2 (#592): the copy button is one complete handler and copies the stored key, whatever characters it has.
        var job = Job(JobStatus.Failed, idempotencyKey: key);
        var html = await DashboardTestHost.GetAsync($"/dashboard/jobs/{job.Id.Value}", s => s.EnqueueAsync(job));

        InlineHandlers.All(html).Select(h => InlineHandlers.CopyValue(h)).Should().Contain(key);
    }

    [Theory]
    [InlineData("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01")]
    [InlineData(Hostile)]
    public async Task CopyTraceParent_CopiesTheStoredValue(string trace)
    {
        // N1 + N2 (#592): the TraceParent copy button copies the stored value.
        var job = Job(JobStatus.Failed, traceParent: trace);
        var html = await DashboardTestHost.GetAsync($"/dashboard/jobs/{job.Id.Value}", s => s.EnqueueAsync(job));

        InlineHandlers.All(html).Select(h => InlineHandlers.CopyValue(h)).Should().Contain(trace);
    }

    [Fact]
    public async Task CopyButtons_WithNoKeyAndNoTrace_RenderNoBrokenHandler()
    {
        // N3 (Boundary): no key and no trace means no copy handler, and no half handler.
        var job = Job(JobStatus.Failed);
        var html = await DashboardTestHost.GetAsync($"/dashboard/jobs/{job.Id.Value}", s => s.EnqueueAsync(job));

        InlineHandlers.Confirms(html).Should().NotContain(h => InlineHandlers.ConfirmMessage(h) == null);
        InlineHandlers.All(html).Where(h => h.Contains("writeText('", StringComparison.Ordinal)).Should().NotContain(h => InlineHandlers.CopyValue(h) == null);
    }
}
