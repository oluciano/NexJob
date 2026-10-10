using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that detects EnqueueAsync / ScheduleAsync calls without explicit queue parameter in multi-service environments.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ImplicitQueuePrefixMismatchAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ016";

    private static readonly DiagnosticDescriptor Rule = AnalyzerHelper.CreateDescriptor(
        DiagnosticId,
        "Enqueue without explicit queue may cause cross-service prefix mismatch",
        "Enqueue call without explicit 'queue' parameter targets default queue; in multi-service architectures with distinct QueuePrefix, specify 'queue' explicitly or align QueuePrefix",
        "Reliability",
        "When services share storage with custom QueuePrefix settings, enqueuing without explicit queue parameter sends jobs to the producer's default queue, which consumer services may never poll.");

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

        if (!AnalyzerHelper.HasNamedOrPositionalArgument(invocation.ArgumentList.Arguments, methodSymbol, "queue"))
        {
            var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }
}
