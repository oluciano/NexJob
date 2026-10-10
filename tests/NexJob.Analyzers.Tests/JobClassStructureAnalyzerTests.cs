using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = NexJob.Analyzers.Tests.CSharpAnalyzerVerifier<NexJob.Analyzers.JobClassStructureAnalyzer>;

namespace NexJob.Analyzers.Tests;

public sealed class JobClassStructureAnalyzerTests
{
    [Fact]
    public async Task N1_Positive_PublicClassWithPublicCtor_NoDiagnostics()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class ValidJob : IJob
{
    public ValidJob() { }

    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N2_Negative_NonPublicOrAbstractClass_ReportsDiagnostic()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

internal class {|#0:InternalJob|} : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public abstract class {|#1:AbstractJob|} : IJob
{
    public abstract Task ExecuteAsync(CancellationToken cancellationToken);
}
";
        var expected0 = new DiagnosticResult("NXJ003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("InternalJob");

        var expected1 = new DiagnosticResult("NXJ003", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(1)
            .WithArguments("AbstractJob");

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0, expected1);
    }

    [Fact]
    public async Task N3_Boundary_InterfaceInheritingIJob_NoDiagnostics()
    {
        const string testCode = @"
using NexJob;

public interface ICustomJob : IJob
{
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
