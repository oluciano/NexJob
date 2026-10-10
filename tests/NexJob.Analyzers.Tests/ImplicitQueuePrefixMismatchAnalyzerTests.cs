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

public sealed class TenantBillingReportJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class BillingCoordinator
{
    public async Task SubmitNamedQueueJob(IScheduler scheduler)
    {
        await scheduler.EnqueueAsync<TenantBillingReportJob>(queue: ""billing-tier1"");
    }

    public async Task SubmitPositionalQueueJob(IScheduler scheduler)
    {
        await scheduler.EnqueueAsync<TenantBillingReportJob>(""billing-tier1"");
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

public sealed class TenantBillingReportJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class BillingCoordinator
{
    public async Task SubmitDefaultQueueJob(IScheduler scheduler)
    {
        await {|#0:scheduler.EnqueueAsync<TenantBillingReportJob>()|};
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

public sealed class RabbitPublisher
{
    public Task EnqueueAsync<TPayload>() => Task.CompletedTask;
}

public sealed class EventForwarder
{
    public async Task SendEvent(RabbitPublisher publisher)
    {
        await publisher.EnqueueAsync<string>();
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

public sealed class TriggerBridge
{
    public async Task EnqueuePrebuiltRecord(IScheduler scheduler, JobRecord record)
    {
        await scheduler.EnqueueAsync(record);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
