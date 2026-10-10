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
    private const string HelpLinkUri = "https://oluciano.github.io/NexJob/guides/best-practices.md";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reliability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: HelpLinkUri);

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
        if (!string.Equals(methodName, "Wait", StringComparison.Ordinal))
        {
            return;
        }

        if (!IsInsideJobMethod(invocation, context.SemanticModel))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol == null || symbol.ContainingType == null)
        {
            return;
        }

        var containingType = symbol.ContainingType.ToDisplayString();
        if (string.Equals(containingType, "System.Threading.Tasks.Task", StringComparison.Ordinal))
        {
            var diagnostic = Diagnostic.Create(Rule, memberAccess.Name.GetLocation(), "Wait");
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

        if (!IsInsideJobMethod(memberAccess, context.SemanticModel))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(memberAccess).Symbol as IPropertySymbol;
        if (symbol == null || symbol.ContainingType == null)
        {
            return;
        }

        var containingType = symbol.ContainingType.OriginalDefinition.ToDisplayString();
        if (string.Equals(containingType, "System.Threading.Tasks.Task<TResult>", StringComparison.Ordinal))
        {
            var diagnostic = Diagnostic.Create(Rule, memberAccess.Name.GetLocation(), "Result");
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsInsideJobMethod(SyntaxNode node, SemanticModel semanticModel)
    {
        var methodDeclaration = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (methodDeclaration == null || !string.Equals(methodDeclaration.Identifier.Text, "ExecuteAsync", StringComparison.Ordinal))
        {
            return false;
        }

        var classDeclaration = methodDeclaration.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDeclaration == null)
        {
            return false;
        }

        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);
        return AnalyzerHelper.ImplementsIJob(classSymbol);
    }
}
