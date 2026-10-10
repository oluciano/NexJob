using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = NexJob.Analyzers.Tests.CSharpAnalyzerVerifier<NexJob.Analyzers.AvoidSwallowingCancellationInJobAnalyzer>;

namespace NexJob.Analyzers.Tests;

public sealed class AvoidSwallowingCancellationInJobAnalyzerTests
{
    [Fact]
    public async Task N1_Positive_CatchCancellationWithRethrow_NoDiagnostics()
    {
        const string testCode = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class GracefulShutdownWorker : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        return Task.CompletedTask;
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N2_Negative_CatchCancellationWithoutRethrow_ReportsDiagnostic()
    {
        const string testCode = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class IgnoringCancellationJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        {|#0:catch (OperationCanceledException)|}
        {
            Console.WriteLine(""Swallowed cancellation token signal"");
        }

        return Task.CompletedTask;
    }
}
";
        var expected0 = new DiagnosticResult("NXJ018", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0);

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0);
    }

    [Fact]
    public async Task N3_Boundary_OutsideJobExecution_NoDiagnostics()
    {
        const string testCode = @"
using System;

public sealed class BackgroundPollingTimer
{
    public void Poll()
    {
        try
        {
            throw new OperationCanceledException();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine(""Non-job cancellation swallow is permitted"");
        }
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
