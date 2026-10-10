using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that recommends specifying an idempotencyKey when enqueueing background jobs.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EncourageIdempotencyKeyAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ011";

    private static readonly DiagnosticDescriptor Rule = AnalyzerHelper.CreateDescriptor(
        DiagnosticId,
        "Consider providing an idempotencyKey for background jobs",
        "Enqueue call does not specify an 'idempotencyKey'; provide an idempotencyKey to prevent duplicate job execution upon network retries or producer replay",
        "Design",
        "In distributed architectures and event-driven triggers, transient network blips and message replays can cause duplicate jobs. Supplying an idempotencyKey guarantees exactly-once active job semantics.");

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

        if (!AnalyzerHelper.IsSchedulerEnqueueOrSchedule(invocation, context.SemanticModel, context.CancellationToken, out var methodSymbol) ||
            methodSymbol == null)
        {
            return;
        }

        if (!AnalyzerHelper.HasNamedOrPositionalArgument(invocation.ArgumentList.Arguments, methodSymbol, "idempotencyKey"))
        {
            var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }
}
