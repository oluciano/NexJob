using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = NexJob.Analyzers.Tests.CSharpAnalyzerVerifier<NexJob.Analyzers.AvoidFireAndForgetInJobAnalyzer>;

namespace NexJob.Analyzers.Tests;

public sealed class AvoidFireAndForgetInJobAnalyzerTests
{
    [Fact]
    public async Task N1_Positive_AwaitedTask_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class GoodJob : IJob
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() => 1 + 1, cancellationToken);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N2_Negative_DiscardedTaskRun_ReportsDiagnostic()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class BadJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _ = {|#0:Task.Run(() => 1 + 1)|};
        return Task.CompletedTask;
    }
}
";
        var expected0 = new DiagnosticResult("NXJ006", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Task.Run");

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0);
    }

    [Fact]
    public async Task N3_Boundary_NonJobDiscardedTask_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading.Tasks;

public sealed class RegularService
{
    public void Fire()
    {
        _ = Task.Run(() => 1 + 1);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
