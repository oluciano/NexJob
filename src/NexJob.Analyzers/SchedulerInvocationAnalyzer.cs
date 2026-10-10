using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Base class for diagnostic analyzers that inspect IScheduler Enqueue and Schedule invocations.
/// </summary>
public abstract class SchedulerInvocationAnalyzer : DiagnosticAnalyzer
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
        AnalyzerHelper.RegisterInvocation(context, AnalyzeNode);
    }

    /// <summary>
    /// Analyzes an IScheduler invocation expression.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="invocation">The invocation expression syntax node.</param>
    /// <param name="methodSymbol">The resolved method symbol of the invocation.</param>
    protected abstract void AnalyzeSchedulerInvocation(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol);

    private void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is InvocationExpressionSyntax invocation &&
            AnalyzerHelper.IsSchedulerEnqueueOrSchedule(invocation, context.SemanticModel, context.CancellationToken, out var methodSymbol) &&
            methodSymbol != null)
        {
            AnalyzeSchedulerInvocation(context, invocation, methodSymbol);
        }
    }
}
