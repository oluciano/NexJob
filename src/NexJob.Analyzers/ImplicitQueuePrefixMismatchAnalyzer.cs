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

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
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

        // Check if invocation explicitly provides an argument for the 'queue' parameter
        var queueParameter = methodSymbol.Parameters.FirstOrDefault(p => string.Equals(p.Name, "queue", StringComparison.Ordinal));
        if (queueParameter == null)
        {
            return;
        }

        var queueParameterIndex = queueParameter.Ordinal;
        var arguments = invocation.ArgumentList.Arguments;

        // 1. Check for named argument: queue: "..."
        for (var i = 0; i < arguments.Count; i++)
        {
            var nameColon = arguments[i].NameColon;
            if (nameColon != null && string.Equals(nameColon.Name.Identifier.Text, "queue", StringComparison.Ordinal))
            {
                // Explicitly provided
                return;
            }
        }

        // 2. Check for positional argument (non-named arguments matching parameter ordinal)
        var positionalCount = 0;
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].NameColon == null)
            {
                if (positionalCount == queueParameterIndex)
                {
                    // Explicitly provided via position
                    return;
                }

                positionalCount++;
            }
        }

        // The queue parameter was omitted (taking default null / default queue)
        var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation());
        context.ReportDiagnostic(diagnostic);
    }
}
