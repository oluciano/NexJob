using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that detects EnqueueAsync / ScheduleAsync calls with deadlineAfter but without an explicit maxAttempts retry bound.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BoundedRetryWithDeadlineAnalyzer : SchedulerInvocationAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ010";

    private static readonly DiagnosticDescriptor Descriptor = AnalyzerHelper.CreateDescriptor(
        DiagnosticId,
        "Enqueue with deadlineAfter should specify bounded maxAttempts",
        "Enqueue call specifies deadlineAfter without explicit maxAttempts; specify maxAttempts to prevent unbounded or repetitive execution within the deadline window",
        "Reliability",
        "When deadlineAfter is set, a job that repeatedly fails without an explicit attempt bound can exhaust resources or loop indefinitely until expiration. Enqueue with an explicit maxAttempts limit.");

    /// <inheritdoc/>
    protected override DiagnosticDescriptor Rule => Descriptor;

    /// <inheritdoc/>
    protected override void AnalyzeSchedulerInvocation(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol)
    {
        var arguments = invocation.ArgumentList.Arguments;
        var hasDeadlineAfter = AnalyzerHelper.HasNamedOrPositionalArgument(arguments, methodSymbol, "deadlineAfter");
        var hasMaxAttempts = AnalyzerHelper.HasNamedOrPositionalArgument(arguments, methodSymbol, "maxAttempts");

        if (hasDeadlineAfter && !hasMaxAttempts)
        {
            var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }
}
