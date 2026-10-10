using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = NexJob.Analyzers.Tests.CSharpAnalyzerVerifier<NexJob.Analyzers.EncourageIdempotencyKeyAnalyzer>;

namespace NexJob.Analyzers.Tests;

public sealed class EncourageIdempotencyKeyAnalyzerTests
{
    [Fact]
    public async Task N1_Positive_EnqueueWithIdempotencyKey_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class MyJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class Service
{
    public async Task EnqueueIdempotent(IScheduler scheduler)
    {
        await scheduler.EnqueueAsync<MyJob>(idempotencyKey: ""order-123"");
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N2_Negative_EnqueueWithoutIdempotencyKey_ReportsDiagnostic()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class MyJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class Service
{
    public async Task EnqueueNonIdempotent(IScheduler scheduler)
    {
        await {|#0:scheduler.EnqueueAsync<MyJob>()|};
    }
}
";
        var expected0 = new DiagnosticResult("NXJ011", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0);

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0);
    }

    [Fact]
    public async Task N3_Boundary_OtherEnqueueCall_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading.Tasks;

public sealed class RegularQueue
{
    public Task EnqueueAsync<T>() => Task.CompletedTask;
}

public sealed class Service
{
    public async Task Send(RegularQueue queue)
    {
        await queue.EnqueueAsync<string>();
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
