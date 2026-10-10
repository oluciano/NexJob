using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = NexJob.Analyzers.Tests.CSharpAnalyzerVerifier<NexJob.Analyzers.ImplicitQueuePrefixMismatchAnalyzer>;

namespace NexJob.Analyzers.Tests;

public sealed class ImplicitQueuePrefixMismatchAnalyzerTests
{
    [Fact]
    public async Task N1_Positive_ExplicitQueueProvided_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class SampleJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class Service
{
    public async Task EnqueueWithQueueNamed(IScheduler scheduler)
    {
        await scheduler.EnqueueAsync<SampleJob>(queue: ""billing"");
    }

    public async Task EnqueueWithQueuePositional(IScheduler scheduler)
    {
        await scheduler.EnqueueAsync<SampleJob>(""billing"");
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N2_Negative_EnqueueWithoutExplicitQueue_ReportsDiagnostic()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class SampleJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class Service
{
    public async Task EnqueueImplicitQueue(IScheduler scheduler)
    {
        await {|#0:scheduler.EnqueueAsync<SampleJob>()|};
    }
}
";
        var expected0 = new DiagnosticResult("NXJ016", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0);

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0);
    }

    [Fact]
    public async Task N3_Boundary_NonSchedulerMethodCall_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading.Tasks;

public sealed class CustomQueueService
{
    public Task EnqueueAsync<T>() => Task.CompletedTask;
}

public sealed class Service
{
    public async Task CallCustomMethod(CustomQueueService custom)
    {
        await custom.EnqueueAsync<string>();
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
