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

    private static readonly DiagnosticDescriptor Rule = AnalyzerHelper.CreateDescriptor(
        DiagnosticId,
        "Avoid direct service instantiation inside job",
        "Do not directly instantiate '{0}' inside job execution; inject dependencies via the job constructor",
        "Design",
        "Background jobs run inside a scoped dependency injection context. Direct instantiation bypasses DI lifetimes, mocking, and scope disposal.");

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

        if (!AnalyzerHelper.IsInsideJobMethod(creation, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        var typeInfo = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type;
        if (typeInfo == null)
        {
            return;
        }

        var typeName = typeInfo.Name;
        // Flag classes ending with Service, Repository, DbContext, or HttpClient
        if (typeName.EndsWith("Service", StringComparison.Ordinal) ||
            typeName.EndsWith("Repository", StringComparison.Ordinal) ||
            typeName.EndsWith("DbContext", StringComparison.Ordinal) ||
            string.Equals(typeName, "HttpClient", StringComparison.Ordinal))
        {
            var diagnostic = Diagnostic.Create(Rule, creation.GetLocation(), typeName);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
