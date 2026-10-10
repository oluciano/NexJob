using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = NexJob.Analyzers.Tests.CSharpAnalyzerVerifier<NexJob.Analyzers.ValidateJobAttributeArgumentsAnalyzer>;

namespace NexJob.Analyzers.Tests;

public sealed class ValidateJobAttributeArgumentsAnalyzerTests
{
    [Fact]
    public async Task N1_Positive_ValidAttributes_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

[Retry(3)]
[Throttle(""payment-gateway"", 5)]
[ExecutionTimeout(""00:05:00"")]
public sealed class ProperlyDecoratedJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

[Retry(0)]
public sealed class ZeroRetryJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N2a_Negative_NegativeRetryAttempts_ReportsDiagnostic()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

[{|#0:Retry(-1)|}]
public sealed class NegativeRetryJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
";
        var expected0 = new DiagnosticResult("NXJ019", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("Retry", "attempts must be greater than or equal to 0");

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0);
    }

    [Fact]
    public async Task N2b_Negative_NonPositiveThrottleLimit_ReportsDiagnostic()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

[{|#0:Throttle(""db-pool"", 0)|}]
public sealed class ZeroThrottleJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
";
        var expected0 = new DiagnosticResult("NXJ019", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("Throttle", "maxConcurrent must be at least 1");

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0);
    }

    [Fact]
    public async Task N2c_Negative_InvalidExecutionTimeoutString_ReportsDiagnostic()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

[{|#0:ExecutionTimeout(""invalid-time"")|}]
public sealed class MalformedTimeoutJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
";
        var expected0 = new DiagnosticResult("NXJ019", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("ExecutionTimeout", "'invalid-time' is not a valid positive TimeSpan");

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0);
    }

    [Fact]
    public async Task N3_Boundary_AttributeOnNonJobClass_NoDiagnostics()
    {
        const string testCode = @"
using NexJob;

[Retry(-5)]
[Throttle("""", 0)]
public sealed class StandaloneUnrelatedService
{
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
