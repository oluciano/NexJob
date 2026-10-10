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

    [Fact]
    public async Task N2_Negative_JobCallingResultOrWait_ReportsDiagnostic()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class BadJob : IJob
{
    private readonly object _gate = new();

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        Task.Delay(10).{|#0:Wait|}();
        var res = Task.FromResult(42).{|#1:Result|};
        {|#2:Thread.Sleep|}(10);
        {|#3:lock|} (_gate)
        {
        }
        return Task.CompletedTask;
    }
}
";
        var expected0 = new DiagnosticResult("NXJ001", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Wait");

        var expected1 = new DiagnosticResult("NXJ001", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(1)
            .WithArguments("Result");

        var expected2 = new DiagnosticResult("NXJ001", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(2)
            .WithArguments("Thread.Sleep");

        var expected3 = new DiagnosticResult("NXJ001", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(3)
            .WithArguments("lock");

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
