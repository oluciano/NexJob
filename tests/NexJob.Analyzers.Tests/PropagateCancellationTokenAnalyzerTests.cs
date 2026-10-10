using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = NexJob.Analyzers.Tests.CSharpAnalyzerVerifier<NexJob.Analyzers.PropagateCancellationTokenAnalyzer>;

namespace NexJob.Analyzers.Tests;

public sealed class PropagateCancellationTokenAnalyzerTests
{
    [Fact]
    public async Task N1_Positive_PassingCancellationToken_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class GoodJob : IJob
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N2_Negative_IgnoringCancellationToken_ReportsDiagnostic()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class BadJob : IJob
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await {|#0:Task.Delay(100)|};
    }
}
";
        var expected0 = new DiagnosticResult("NXJ004", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("Task.Delay");

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0);
    }

    [Fact]
    public async Task N3_Boundary_NonJobClassCallingDelay_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading.Tasks;

public sealed class RegularService
{
    public async Task DoWorkAsync()
    {
        await Task.Delay(100);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
