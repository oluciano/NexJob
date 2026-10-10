using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that detects self-referencing job recursion (a job enqueueing itself from within its own execution).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PreventRecursiveJobContinuationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ015";

    private static readonly DiagnosticDescriptor Rule = AnalyzerHelper.CreateDescriptor(
        DiagnosticId,
        "Prevent recursive job continuation",
        "Job '{0}' directly enqueues itself from within ExecuteAsync; use recurring jobs or bounded iterations instead of unbounded self-referencing continuations",
        "Reliability",
        "Direct self-continuation creates infinite execution feedback loops that flood queues and bypass scheduling cadence controls.");

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

        if (!AnalyzerHelper.IsInsideJobMethod(invocation, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        if (!AnalyzerHelper.IsSchedulerEnqueueOrSchedule(invocation, context.SemanticModel, context.CancellationToken, out var methodSymbol) ||
            methodSymbol == null)
        {
            return;
        }

        // Get the enclosing job type symbol
        var enclosingTypeDeclaration = invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (enclosingTypeDeclaration == null)
        {
            return;
        }

        var enclosingTypeSymbol = context.SemanticModel.GetDeclaredSymbol(enclosingTypeDeclaration, context.CancellationToken);
        if (enclosingTypeSymbol == null)
        {
            return;
        }

        // Check if the generic type argument of EnqueueAsync<TJob> matches the enclosing job type
        if (methodSymbol.TypeArguments.Length > 0)
        {
            var targetJobType = methodSymbol.TypeArguments[0];
            if (SymbolEqualityComparer.Default.Equals(targetJobType, enclosingTypeSymbol))
            {
                var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation(), enclosingTypeSymbol.Name);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }
}
