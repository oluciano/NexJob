using FluentAssertions;
using Xunit;

namespace NexJob.Tests;

public sealed class FailedPageTests
{
    [Fact]
    public async Task RequeueAll_Confirmation_IsACompleteHandler()
    {
        // N1 (#592): the Requeue All button asks for confirmation with a complete handler.
        var html = await DashboardTestHost.GetAsync("/dashboard/failed", s =>
            s.EnqueueAsync(DashboardTestHost.Job("Acme.Jobs.ReportJob", "default", JobStatus.Failed)));

        var confirms = InlineHandlers.Confirms(html);
        confirms.Should().NotContain(h => InlineHandlers.ConfirmMessage(h) == null);
        confirms.Select(h => InlineHandlers.ConfirmMessage(h)).Should().Contain(m => m != null && m.StartsWith("Requeue all failed jobs?", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RequeueAll_WithNoFailedJobs_RendersNoBrokenHandler()
    {
        // N2 (Negative): an empty Failed page never leaves a half handler behind.
        var html = await DashboardTestHost.GetAsync("/dashboard/failed");

        InlineHandlers.Confirms(html).Should().NotContain(h => InlineHandlers.ConfirmMessage(h) == null);
    }

    [Fact]
    public async Task RequeueAll_OnAnUnknownStatusFilter_FallsBackToAWellFormedHandler()
    {
        // N3 (Invalid Input): a status that is not a JobStatus does not break the handler.
        var html = await DashboardTestHost.GetAsync("/dashboard/failed?status=%27%22%5C", s =>
            s.EnqueueAsync(DashboardTestHost.Job("Acme.Jobs.ReportJob", "default", JobStatus.Failed)));

        InlineHandlers.Confirms(html).Should().NotContain(h => InlineHandlers.ConfirmMessage(h) == null);
    }
}
