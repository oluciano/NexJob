using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that detects swallowed OperationCanceledException in job execution.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidSwallowingCancellationInJobAnalyzer : CatchClauseJobAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ018";

    private static readonly DiagnosticDescriptor Descriptor = AnalyzerHelper.CreateDescriptor(
        DiagnosticId,
        "Avoid swallowing OperationCanceledException in job execution",
        "OperationCanceledException is caught and swallowed in job execution without rethrowing; cancellation must propagate so NexJob can requeue the job on graceful shutdown",
        "Reliability",
        "Catching and swallowing OperationCanceledException prevents the host and worker dispatcher from properly requeuing the job during graceful shutdown.");

    /// <inheritdoc/>
    protected override DiagnosticDescriptor Rule => Descriptor;

    /// <inheritdoc/>
    protected override void AnalyzeJobCatch(
        SyntaxNodeAnalysisContext context,
        CatchClauseSyntax catchClause,
        ITypeSymbol? caughtTypeSymbol)
    {
        if (caughtTypeSymbol == null)
        {
            return;
        }

        var isCancellation = IsCancellationException(caughtTypeSymbol);
        if (isCancellation && !CatchBlockRethrows(catchClause))
        {
            var location = GetCatchHeaderLocation(catchClause);
            context.ReportDiagnostic(Diagnostic.Create(Rule, location));
        }
    }

    private static bool IsCancellationException(ITypeSymbol typeSymbol)
    {
        for (var current = typeSymbol; current != null; current = current.BaseType)
        {
            if (string.Equals(current.Name, "OperationCanceledException", StringComparison.Ordinal) ||
                string.Equals(current.Name, "TaskCanceledException", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
