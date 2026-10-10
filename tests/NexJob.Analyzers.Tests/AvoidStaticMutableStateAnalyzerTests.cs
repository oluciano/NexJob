using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = NexJob.Analyzers.Tests.CSharpAnalyzerVerifier<NexJob.Analyzers.AvoidStaticMutableStateAnalyzer>;

namespace NexJob.Analyzers.Tests;

public sealed class AvoidStaticMutableStateAnalyzerTests
{
    [Fact]
    public async Task N1_Positive_StaticReadonlyOrConstField_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class GoodJob : IJob
{
    private const int MaxBatchSize = 100;
    private static readonly object Gate = new();

    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N2_Negative_StaticMutableField_ReportsDiagnostic()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class BadJob : IJob
{
    private static int {|#0:ExecutionCounter|};
    public static string? {|#1:LastMessage|};

    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
";
        var expected0 = new DiagnosticResult("NXJ005", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("ExecutionCounter");

        var expected1 = new DiagnosticResult("NXJ005", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(1)
            .WithArguments("LastMessage");

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0, expected1);
    }

    [Fact]
    public async Task N3_Boundary_NonJobClassWithStaticField_NoDiagnostics()
    {
        const string testCode = @"
public static class GlobalState
{
    public static int Counter = 0;
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
