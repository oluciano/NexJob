using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = NexJob.Analyzers.Tests.CSharpAnalyzerVerifier<NexJob.Analyzers.AvoidSwallowingExceptionsInJobAnalyzer>;

namespace NexJob.Analyzers.Tests;

public sealed class AvoidSwallowingExceptionsInJobAnalyzerTests
{
    [Fact]
    public async Task N1_Positive_CatchWithRethrow_NoDiagnostics()
    {
        const string testCode = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class SafePaymentJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine(""Processing"");
        }
        catch (Exception)
        {
            Console.WriteLine(""Logging before throw"");
            throw;
        }

        return Task.CompletedTask;
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N2_Negative_CatchExceptionWithoutRethrow_ReportsDiagnostic()
    {
        const string testCode = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class SwallowingJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine(""Unsafe"");
        }
        {|#0:catch (Exception)|}
        {
            Console.WriteLine(""Swallowed"");
        }

        return Task.CompletedTask;
    }
}
";
        var expected0 = new DiagnosticResult("NXJ017", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0);

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0);
    }

    [Fact]
    public async Task N2b_Negative_GeneralUntypedCatch_ReportsDiagnostic()
    {
        const string testCode = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class UntypedSwallowingJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine(""Unsafe"");
        }
        {|#0:catch|}
        {
            Console.WriteLine(""Untyped swallow"");
        }

        return Task.CompletedTask;
    }
}
";
        var expected0 = new DiagnosticResult("NXJ017", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0);

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0);
    }

    [Fact]
    public async Task N3_Boundary_OutsideJobExecution_NoDiagnostics()
    {
        const string testCode = @"
using System;
using System.Threading.Tasks;

public sealed class StandardController
{
    public void HandleRequest()
    {
        try
        {
            Console.WriteLine(""Normal app method"");
        }
        catch (Exception)
        {
            Console.WriteLine(""Swallowed in non-job method"");
        }
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
