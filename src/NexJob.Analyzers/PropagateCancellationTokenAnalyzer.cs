using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that flags async invocations inside IJob.ExecuteAsync that omit CancellationToken.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PropagateCancellationTokenAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ004";

    private const string Title = "Propagate CancellationToken in job execution";
    private const string MessageFormat = "Call '{0}' should propagate the available CancellationToken";
    private const string Description = "Background jobs should pass the execution CancellationToken to support graceful shutdown and timeouts.";
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
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        var methodDeclaration = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (methodDeclaration == null || !string.Equals(methodDeclaration.Identifier.Text, "ExecuteAsync", StringComparison.Ordinal))
        {
            return;
        }

        var classDeclaration = methodDeclaration.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDeclaration == null)
        {
            return;
        }

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
        if (!AnalyzerHelper.ImplementsIJob(classSymbol))
        {
            return;
        }

        var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (methodSymbol == null)
        {
            return;
        }

        // Check if current invocation already passes a CancellationToken
        var alreadyPassesCancellationToken = methodSymbol.Parameters.Any(p =>
            string.Equals(p.Type.ToDisplayString(), "System.Threading.CancellationToken", StringComparison.Ordinal));

        if (alreadyPassesCancellationToken)
        {
            return;
        }

        // Check if the containing type offers an overload that accepts CancellationToken
        var hasCancellationTokenOverload = methodSymbol.ContainingType
            .GetMembers(methodSymbol.Name)
            .OfType<IMethodSymbol>()
            .Any(m => m.Parameters.Any(p => string.Equals(p.Type.ToDisplayString(), "System.Threading.CancellationToken", StringComparison.Ordinal)));

        if (hasCancellationTokenOverload)
        {
            var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation(), invocation.Expression.ToString());
            context.ReportDiagnostic(diagnostic);
        }
    }
}
