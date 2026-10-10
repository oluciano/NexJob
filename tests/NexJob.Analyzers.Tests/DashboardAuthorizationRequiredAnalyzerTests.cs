using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = NexJob.Analyzers.Tests.CSharpAnalyzerVerifier<NexJob.Analyzers.DashboardAuthorizationRequiredAnalyzer>;

namespace NexJob.Analyzers.Tests;

public sealed class DashboardAuthorizationRequiredAnalyzerTests
{
    [Fact]
    public async Task N1_Positive_ConfiguringDashboardWithOptions_NoDiagnostics()
    {
        const string testCode = @"
using Microsoft.AspNetCore.Builder;
using NexJob.Dashboard;

public sealed class Startup
{
    public void Configure(IApplicationBuilder app)
    {
        app.UseNexJobDashboard(""/dashboard"", options =>
        {
            options.AuthorizationHandler = null;
        });
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task N2_Negative_CallingDashboardWithoutConfigureAction_ReportsDiagnostic()
    {
        const string testCode = @"
using Microsoft.AspNetCore.Builder;

public sealed class Startup
{
    public void Configure(IApplicationBuilder app)
    {
        {|#0:app.UseNexJobDashboard()|};
    }
}
";
        var expected0 = new DiagnosticResult("NXJ008", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("app.UseNexJobDashboard");

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected0);
    }

    [Fact]
    public async Task N3_Boundary_OtherMiddlewareMethod_NoDiagnostics()
    {
        const string testCode = @"
public sealed class OtherApp
{
    public void UseCustomDashboard() { }
}

public sealed class Startup
{
    public void Configure(OtherApp app)
    {
        app.UseCustomDashboard();
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}
