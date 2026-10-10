using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = NexJob.Analyzers.Tests.CSharpAnalyzerVerifier<NexJob.Analyzers.AvoidInMemoryStorageInProductionAnalyzer>;

namespace NexJob.Analyzers.Tests;

public sealed class AvoidInMemoryStorageInProductionAnalyzerTests
{
    [Fact]
    public async Task N1_Positive_OtherServiceCollectionCall_NoDiagnostics()
    {
        const string testCode = @"
public sealed class Startup
{
    public void ConfigureServices(object services)
    {
        // Custom non-NexJob configuration
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N2_Negative_CallingAddNexJobDirectly_ReportsDiagnostic()
    {
        const string testCode = @"
using NexJob;

public sealed class Startup
{
    public void ConfigureServices(object services)
    {
        {|#0:services.AddNexJob()|};
    }
}
";
        var expected0 = new DiagnosticResult("NXJ009", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("services.AddNexJob");

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0);
    }

    [Fact]
    public async Task N3_Boundary_CustomAddNexJobMethod_NoDiagnostics()
    {
        const string testCode = @"
public static class CustomExtensions
{
    public static object AddNexJob(this object services, string marker) => services;
}

public sealed class Startup
{
    public void ConfigureServices(object services)
    {
        services.AddNexJob(""custom"");
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
