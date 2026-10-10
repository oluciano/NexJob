using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = NexJob.Analyzers.Tests.CSharpAnalyzerVerifier<NexJob.Analyzers.AvoidDirectServiceInstantiationAnalyzer>;

namespace NexJob.Analyzers.Tests;

public sealed class AvoidDirectServiceInstantiationAnalyzerTests
{
    [Fact]
    public async Task N1_Positive_ConstructorInjectedServiceOrPrimitiveInstantiation_NoDiagnostics()
    {
        const string testCode = @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public sealed class GoodJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var list = new List<string>();
        return Task.CompletedTask;
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N2_Negative_InstantiatingServiceOrRepositoryDirectly_ReportsDiagnostic()
    {
        const string testCode = @"
using System.Threading;
using System.Threading.Tasks;
using NexJob;

public class EmailService { }
public class OrderRepository { }

public sealed class BadJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var email = {|#0:new EmailService()|};
        var repo = {|#1:new OrderRepository()|};
        return Task.CompletedTask;
    }
}
";
        var expected0 = new DiagnosticResult("NXJ007", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("EmailService");

        var expected1 = new DiagnosticResult("NXJ007", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(1)
            .WithArguments("OrderRepository");

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0, expected1);
    }

    [Fact]
    public async Task N3_Boundary_NonJobClassDirectInstantiation_NoDiagnostics()
    {
        const string testCode = @"
public class EmailService { }

public sealed class Factory
{
    public EmailService Create() => new EmailService();
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
