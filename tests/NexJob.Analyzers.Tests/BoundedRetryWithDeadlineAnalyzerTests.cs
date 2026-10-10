using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = NexJob.Analyzers.Tests.CSharpAnalyzerVerifier<NexJob.Analyzers.BoundedRetryWithDeadlineAnalyzer>;

namespace NexJob.Analyzers.Tests;

public sealed class BoundedRetryWithDeadlineAnalyzerTests
{
    [Fact]
    public async Task N1_Positive_DeadlineAfterWithExplicitMaxAttempts_NoDiagnostics()
    {
        const string testCode = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class DeadlineProcessingJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class DeadlineDispatchService
{
    public async Task EnqueueBoundedRetry(IScheduler scheduler)
    {
        await scheduler.EnqueueAsync<DeadlineProcessingJob>(maxAttempts: 3, deadlineAfter: TimeSpan.FromMinutes(5));
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N2_Negative_DeadlineAfterWithoutMaxAttempts_ReportsDiagnostic()
    {
        const string testCode = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class DeadlineProcessingJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class DeadlineDispatchService
{
    public async Task EnqueueUnboundedRetry(IScheduler scheduler)
    {
        await {|#0:scheduler.EnqueueAsync<DeadlineProcessingJob>(deadlineAfter: TimeSpan.FromMinutes(5))|};
    }
}
";
        var expected0 = new DiagnosticResult("NXJ010", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0);

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0);
    }

    [Fact]
    public async Task N3_Boundary_EnqueueWithoutDeadline_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class DeadlineProcessingJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class DeadlineDispatchService
{
    public async Task EnqueueStandardJob(IScheduler scheduler)
    {
        await scheduler.EnqueueAsync<DeadlineProcessingJob>();
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N4_Boundary_JobRecordOverload_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading.Tasks;
using NexJob;

public sealed class IngestionBridge
{
    public async Task ForwardJobRecord(IScheduler scheduler, JobRecord record)
    {
        await scheduler.EnqueueAsync(record);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
