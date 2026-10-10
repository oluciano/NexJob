using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Base class for analyzers detecting missing optional arguments in scheduler invocations.
/// </summary>
public abstract class MissingSchedulerArgumentAnalyzer : SchedulerInvocationAnalyzer
{
    private readonly string _targetParameter;

    /// <summary>
    /// Initializes a new instance of the <see cref="MissingSchedulerArgumentAnalyzer"/> class.
    /// </summary>
    /// <param name="targetParameter">The parameter name that must be explicitly supplied.</param>
    protected MissingSchedulerArgumentAnalyzer(string targetParameter)
    {
        _targetParameter = targetParameter;
    }

    /// <inheritdoc/>
    protected sealed override void AnalyzeSchedulerInvocation(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol)
    {
        if (AnalyzerHelper.HasParameter(methodSymbol, _targetParameter) &&
            !AnalyzerHelper.HasNamedOrPositionalArgument(invocation.ArgumentList.Arguments, methodSymbol, _targetParameter))
        {
            var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }
}
