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

public sealed class FirstJob : IJob
{
    private readonly IScheduler _scheduler;
    public FirstJob(IScheduler scheduler) => _scheduler = scheduler;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await _scheduler.EnqueueAsync<SecondJob>();
    }
}

public sealed class SecondJob : IJob
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

public sealed class RecursiveJob : IJob
{
    private readonly IScheduler _scheduler;
    public RecursiveJob(IScheduler scheduler) => _scheduler = scheduler;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await {|#0:_scheduler.EnqueueAsync<RecursiveJob>()|};
    }
}
";
        var expected0 = new DiagnosticResult("NXJ015", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("RecursiveJob");

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0);
    }

    [Fact]
    public async Task N3_Boundary_EnqueueOutsideJob_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class MyJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class OrderController
{
    public async Task CreateOrder(IScheduler scheduler)
    {
        await scheduler.EnqueueAsync<MyJob>();
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
