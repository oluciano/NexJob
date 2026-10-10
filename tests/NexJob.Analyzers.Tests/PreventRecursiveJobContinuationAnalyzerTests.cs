using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = NexJob.Analyzers.Tests.CSharpAnalyzerVerifier<NexJob.Analyzers.PreventRecursiveJobContinuationAnalyzer>;

namespace NexJob.Analyzers.Tests;

public sealed class PreventRecursiveJobContinuationAnalyzerTests
{
    [Fact]
    public async Task N1_Positive_JobEnqueuingDifferentJob_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class StageOneJob : IJob
{
    private readonly IScheduler _scheduler;
    public StageOneJob(IScheduler scheduler) => _scheduler = scheduler;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await _scheduler.EnqueueAsync<StageTwoJob>();
    }
}

public sealed class StageTwoJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N2_Negative_JobEnqueuingItselfDirectly_ReportsDiagnostic()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class SelfLoopingJob : IJob
{
    private readonly IScheduler _scheduler;
    public SelfLoopingJob(IScheduler scheduler) => _scheduler = scheduler;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await {|#0:_scheduler.EnqueueAsync<SelfLoopingJob>()|};
    }
}
";
        var expected0 = new DiagnosticResult("NXJ015", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("SelfLoopingJob");

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0);
    }

    [Fact]
    public async Task N3_Boundary_EnqueueOutsideJob_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class StandaloneActionJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class WebCheckoutController
{
    public async Task ProcessCheckout(IScheduler scheduler)
    {
        await scheduler.EnqueueAsync<StandaloneActionJob>();
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N4_Boundary_JobRecordEnqueue_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class RecordForwardingJob : IJob
{
    private readonly IScheduler _scheduler;
    public RecordForwardingJob(IScheduler scheduler) => _scheduler = scheduler;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await _scheduler.EnqueueAsync(new JobRecord());
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
