using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that detects bare AddNexJob() calls in application entry points without storage providers.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidInMemoryStorageInProductionAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ009";

    private static readonly DiagnosticDescriptor Rule = AnalyzerHelper.CreateDescriptor(
        DiagnosticId,
        "InMemory storage is not safe for distributed multi-node deployments",
        "Calling '{0}' uses default in-memory storage; configure a persistent storage provider (PostgreSQL, SQL Server, Redis, MongoDB) for production multi-node environments",
        "Reliability",
        "InMemory storage does not persist jobs across application restarts and cannot synchronize execution state across multi-node clusters.");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        AnalyzerHelper.RegisterInvocation(context, AnalyzeInvocation);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
        if (methodSymbol == null)
        {
            return;
        }

        if (!string.Equals(methodSymbol.Name, "AddNexJob", StringComparison.Ordinal))
        {
            return;
        }

        var containingType = methodSymbol.ContainingType;
        if (containingType == null || !string.Equals(containingType.Name, "NexJobServiceCollectionExtensions", StringComparison.Ordinal))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation(), invocation.Expression.ToString());
        context.ReportDiagnostic(diagnostic);
    }
}
