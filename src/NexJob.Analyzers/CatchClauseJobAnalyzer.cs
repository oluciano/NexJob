using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NexJob.Analyzers;

/// <summary>
/// Base class for analyzers that inspect catch clauses within job execution methods.
/// </summary>
public abstract class CatchClauseJobAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public sealed override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <summary>
    /// Gets the diagnostic descriptor supported by this analyzer.
    /// </summary>
    protected abstract DiagnosticDescriptor Rule { get; }

    /// <inheritdoc/>
    public sealed override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeCatchClause, Microsoft.CodeAnalysis.CSharp.SyntaxKind.CatchClause);
    }

    /// <summary>
    /// Checks whether the catch clause contains a throw statement or throw expression.
    /// </summary>
    /// <param name="catchClause">The catch clause to inspect.</param>
    /// <returns>True if the catch block contains a throw; otherwise, false.</returns>
    protected static bool CatchBlockRethrows(CatchClauseSyntax catchClause)
    {
        return catchClause.DescendantNodes().Any(node =>
            node is ThrowStatementSyntax || node is ThrowExpressionSyntax);
    }

    /// <summary>
    /// Gets the diagnostic location spanning the catch keyword and its optional declaration.
    /// </summary>
    /// <param name="catchClause">The catch clause syntax node.</param>
    /// <returns>A location spanning the catch header.</returns>
    protected static Location GetCatchHeaderLocation(CatchClauseSyntax catchClause)
    {
        var start = catchClause.CatchKeyword.SpanStart;
        var end = catchClause.Declaration?.Span.End ?? catchClause.CatchKeyword.Span.End;
        return Location.Create(catchClause.SyntaxTree, TextSpan.FromBounds(start, end));
    }

    /// <summary>
    /// Analyzes a catch clause occurring within an IJob ExecuteAsync method.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="catchClause">The catch clause syntax node.</param>
    /// <param name="caughtTypeSymbol">The resolved type symbol of the caught exception, if specified.</param>
    protected abstract void AnalyzeJobCatch(
        SyntaxNodeAnalysisContext context,
        CatchClauseSyntax catchClause,
        ITypeSymbol? caughtTypeSymbol);

    private void AnalyzeCatchClause(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not CatchClauseSyntax catchClause)
        {
            return;
        }

        if (!AnalyzerHelper.IsInsideJobMethod(catchClause, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        ITypeSymbol? caughtTypeSymbol = null;
        if (catchClause.Declaration?.Type != null)
        {
            caughtTypeSymbol = context.SemanticModel.GetTypeInfo(catchClause.Declaration.Type, context.CancellationToken).Type;
        }

        AnalyzeJobCatch(context, catchClause, caughtTypeSymbol);
    }
}
