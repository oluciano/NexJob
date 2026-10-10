using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = NexJob.Analyzers.Tests.CSharpAnalyzerVerifier<NexJob.Analyzers.AvoidBlockingCallsAnalyzer>;

namespace NexJob.Analyzers.Tests;

public sealed class AvoidBlockingCallsAnalyzerTests
{
    [Fact]
    public async Task N1_Positive_AsyncJobCallingAwait_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class GoodJob : IJob
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    // Behavior changed in v6.2.0: lock was removed (valid for short critical sections), GetAwaiter().GetResult() and WaitAll/WaitAny added, severity changed to Info
    [Fact]
    public async Task N2_Negative_JobCallingResultOrWaitOrGetResult_ReportsDiagnostic()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class BadJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        Task.Delay(10).{|#0:Wait|}();
        var res = Task.FromResult(42).{|#1:Result|};
        {|#2:Thread.Sleep|}(10);
        var val = Task.FromResult(1).GetAwaiter().{|#3:GetResult|}();
        return Task.CompletedTask;
    }
}
";
        var expected0 = new DiagnosticResult("NXJ001", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("Wait");

        var expected1 = new DiagnosticResult("NXJ001", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(1)
            .WithArguments("Result");

        var expected2 = new DiagnosticResult("NXJ001", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(2)
            .WithArguments("Thread.Sleep");

        var expected3 = new DiagnosticResult("NXJ001", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(3)
            .WithArguments("GetAwaiter().GetResult()");

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0, expected1, expected2, expected3);
    }

    [Fact]
    public async Task N3_Boundary_NonJobClassCallingWait_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading.Tasks;

public sealed class RegularService
{
    public void DoWork()
    {
        Task.Delay(10).Wait();
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
