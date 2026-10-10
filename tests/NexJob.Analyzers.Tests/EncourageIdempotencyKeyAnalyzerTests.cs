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

public sealed class OrderFulfillmentJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class OrderDispatchService
{
    public async Task ScheduleOrderFulfillment(IScheduler scheduler)
    {
        await scheduler.EnqueueAsync<OrderFulfillmentJob>(idempotencyKey: ""order-987"");
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

public sealed class OrderFulfillmentJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class OrderDispatchService
{
    public async Task DispatchUnkeyedOrder(IScheduler scheduler)
    {
        await {|#0:scheduler.EnqueueAsync<OrderFulfillmentJob>()|};
    }
}
";
        var expected0 = new DiagnosticResult("NXJ011", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0);

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0);
    }

    [Fact]
    public async Task N3_Boundary_UnrelatedQueueMethod_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading.Tasks;

public sealed class MemoryChannelWriter
{
    public Task EnqueueAsync<T>() => Task.CompletedTask;
}

public sealed class PipelineComponent
{
    public async Task DelegateToChannel(MemoryChannelWriter writer)
    {
        await writer.EnqueueAsync<int>();
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

public sealed class RawBrokerBridge
{
    public async Task EnqueueJobRecordDirectly(IScheduler scheduler, JobRecord record)
    {
        await scheduler.EnqueueAsync(record);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
