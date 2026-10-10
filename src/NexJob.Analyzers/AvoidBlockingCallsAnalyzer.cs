using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that flags blocking calls inside IJob.ExecuteAsync.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidBlockingCallsAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ001";

    private const string Title = "Avoid blocking calls in job execution";
    private const string MessageFormat = "Avoid blocking call '{0}' in background job execution; use await instead";
    private const string Description = "Background jobs must be fully asynchronous to avoid thread pool starvation.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reliability",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: AnalyzerHelper.HelpBaseUrl);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var methodName = memberAccess.Name.Identifier.Text;
        var isWait = string.Equals(methodName, "Wait", StringComparison.Ordinal);
        var isWaitAllOrAny = string.Equals(methodName, "WaitAll", StringComparison.Ordinal) ||
                             string.Equals(methodName, "WaitAny", StringComparison.Ordinal);
        var isSleep = string.Equals(methodName, "Sleep", StringComparison.Ordinal);
        var isGetResult = string.Equals(methodName, "GetResult", StringComparison.Ordinal);

        if (!isWait && !isWaitAllOrAny && !isSleep && !isGetResult)
        {
            return;
        }

        if (!AnalyzerHelper.IsInsideJobMethod(invocation, context.SemanticModel))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol == null || symbol.ContainingType == null)
        {
            return;
        }

        var containingType = symbol.ContainingType.ToDisplayString();
        if (isWait && (string.Equals(containingType, "System.Threading.Tasks.Task", StringComparison.Ordinal) ||
                       string.Equals(containingType, "System.Threading.Tasks.ValueTask", StringComparison.Ordinal)))
        {
            var diagnostic = Diagnostic.Create(Rule, memberAccess.Name.GetLocation(), "Wait");
            context.ReportDiagnostic(diagnostic);
        }
        else if (isWaitAllOrAny && string.Equals(containingType, "System.Threading.Tasks.Task", StringComparison.Ordinal))
        {
            var diagnostic = Diagnostic.Create(Rule, memberAccess.GetLocation(), $"Task.{methodName}");
            context.ReportDiagnostic(diagnostic);
        }
        else if (isSleep && string.Equals(containingType, "System.Threading.Thread", StringComparison.Ordinal))
        {
            var diagnostic = Diagnostic.Create(Rule, memberAccess.GetLocation(), "Thread.Sleep");
            context.ReportDiagnostic(diagnostic);
        }
        else if (isGetResult && (containingType.StartsWith("System.Runtime.CompilerServices.TaskAwaiter", StringComparison.Ordinal) ||
                                 containingType.StartsWith("System.Runtime.CompilerServices.ValueTaskAwaiter", StringComparison.Ordinal)))
        {
            var diagnostic = Diagnostic.Create(Rule, memberAccess.Name.GetLocation(), "GetAwaiter().GetResult()");
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var memberName = memberAccess.Name.Identifier.Text;
        if (!string.Equals(memberName, "Result", StringComparison.Ordinal))
        {
            return;
        }

        if (!AnalyzerHelper.IsInsideJobMethod(memberAccess, context.SemanticModel))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(memberAccess).Symbol as IPropertySymbol;
        if (symbol == null || symbol.ContainingType == null)
        {
            return;
        }

        var containingType = symbol.ContainingType.OriginalDefinition.ToDisplayString();
        if (string.Equals(containingType, "System.Threading.Tasks.Task<TResult>", StringComparison.Ordinal) ||
            string.Equals(containingType, "System.Threading.Tasks.ValueTask<TResult>", StringComparison.Ordinal))
        {
            var diagnostic = Diagnostic.Create(Rule, memberAccess.Name.GetLocation(), "Result");
            context.ReportDiagnostic(diagnostic);
        }
    }
}
