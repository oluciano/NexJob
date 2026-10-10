using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that detects swallowed exceptions in job execution.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidSwallowingExceptionsInJobAnalyzer : CatchClauseJobAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ017";

    private static readonly DiagnosticDescriptor Descriptor = AnalyzerHelper.CreateDescriptor(
        DiagnosticId,
        "Avoid swallowing exceptions in job execution",
        "Exception is caught and swallowed in job execution without rethrowing; unhandled exceptions must propagate so NexJob can manage retry and dead-letter lifecycles",
        "Reliability",
        "When an exception is swallowed without being rethrown, the job terminates as succeeded, preventing retry attempts, dead-letter dispatch, and alerting.");

    /// <inheritdoc/>
    protected override DiagnosticDescriptor Rule => Descriptor;

    /// <inheritdoc/>
    protected override void AnalyzeJobCatch(
        SyntaxNodeAnalysisContext context,
        CatchClauseSyntax catchClause,
        ITypeSymbol? caughtTypeSymbol)
    {
        var isGeneralCatch = catchClause.Declaration == null ||
                             caughtTypeSymbol == null ||
                             string.Equals(caughtTypeSymbol.MetadataName, "Exception", StringComparison.Ordinal) ||
                             string.Equals(caughtTypeSymbol.MetadataName, "Object", StringComparison.Ordinal);

        if (isGeneralCatch && !CatchBlockRethrows(catchClause))
        {
            var location = GetCatchHeaderLocation(catchClause);
            context.ReportDiagnostic(Diagnostic.Create(Rule, location));
        }
    }
}
