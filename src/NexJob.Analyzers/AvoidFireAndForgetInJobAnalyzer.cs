using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that flags fire-and-forget task launches inside IJob.ExecuteAsync.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidFireAndForgetInJobAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ006";

    private const string Title = "Avoid fire-and-forget tasks in job execution";
    private const string MessageFormat = "Do not fire-and-forget '{0}' inside a background job; await the task or enqueue a job continuation";
    private const string Description = "Background jobs must be fully awaited. Fire-and-forget tasks escape the execution lifecycle, deadline tracking, and error handling of NexJob.";
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

        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not AssignmentExpressionSyntax assignment)
        {
            return;
        }

        // Check if left-hand side is a discard expression `_ = ...`
        if (assignment.Left is not IdentifierNameSyntax leftIdentifier || !string.Equals(leftIdentifier.Identifier.Text, "_", StringComparison.Ordinal))
        {
            return;
        }

        if (assignment.Right is not InvocationExpressionSyntax invocation)
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

        var returnType = methodSymbol.ReturnType.ToDisplayString();
        if (returnType.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal) ||
            returnType.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal))
        {
            var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation(), invocation.Expression.ToString());
            context.ReportDiagnostic(diagnostic);
        }
    }
}
