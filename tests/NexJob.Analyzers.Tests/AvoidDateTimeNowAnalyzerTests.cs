using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = NexJob.Analyzers.Tests.CSharpAnalyzerVerifier<NexJob.Analyzers.AvoidDateTimeNowAnalyzer>;

namespace NexJob.Analyzers.Tests;

public sealed class AvoidDateTimeNowAnalyzerTests
{
    [Fact]
    public async Task N1_Positive_JobUsingUtcNow_NoDiagnostics()
    {
        const string testCode = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class GoodJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var offset = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N2_Negative_JobUsingDateTimeNow_ReportsDiagnostic()
    {
        const string testCode = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class BadJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var t1 = {|#0:DateTime.Now|};
        var t2 = {|#1:DateTime.Today|};
        return Task.CompletedTask;
    }
}
";
        var expected0 = new DiagnosticResult("NXJ002", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("DateTime.Now");

        var expected1 = new DiagnosticResult("NXJ002", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(1)
            .WithArguments("DateTime.Today");

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0, expected1);
    }

    [Fact]
    public async Task N3_Boundary_CustomPropertyNamedNow_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class CustomClock
{
    public int Now => 123;
}

public sealed class SafeJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var clock = new CustomClock();
        var x = clock.Now;
        return Task.CompletedTask;
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
