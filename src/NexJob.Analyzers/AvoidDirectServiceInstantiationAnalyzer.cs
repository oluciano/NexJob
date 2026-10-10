using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that flags direct service/repository instantiation inside IJob.ExecuteAsync.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidDirectServiceInstantiationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ007";

    private const string Title = "Avoid direct service instantiation inside job";
    private const string MessageFormat = "Do not directly instantiate '{0}' inside job execution; inject dependencies via the job constructor";
    private const string Description = "Background jobs run inside a scoped dependency injection context. Direct instantiation bypasses DI lifetimes, mocking, and scope disposal.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Design",
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

        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ObjectCreationExpressionSyntax creation)
        {
            return;
        }

        if (!AnalyzerHelper.IsInsideJobMethod(creation, context.SemanticModel))
        {
            return;
        }

        var typeInfo = context.SemanticModel.GetTypeInfo(creation).Type;
        if (typeInfo == null)
        {
            return;
        }

        var typeName = typeInfo.Name;
        // Flag classes ending with Service, Repository, Handler, Client, or Manager
        if (typeName.EndsWith("Service", StringComparison.Ordinal) ||
            typeName.EndsWith("Repository", StringComparison.Ordinal) ||
            typeName.EndsWith("Handler", StringComparison.Ordinal) ||
            typeName.EndsWith("Client", StringComparison.Ordinal) ||
            typeName.EndsWith("Manager", StringComparison.Ordinal) ||
            typeName.EndsWith("DbContext", StringComparison.Ordinal))
        {
            var diagnostic = Diagnostic.Create(Rule, creation.GetLocation(), typeName);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
