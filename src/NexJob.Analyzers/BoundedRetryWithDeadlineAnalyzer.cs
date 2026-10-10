using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that detects EnqueueAsync / ScheduleAsync calls with deadlineAfter but without an explicit maxAttempts retry bound.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BoundedRetryWithDeadlineAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ010";

    private static readonly DiagnosticDescriptor Rule = AnalyzerHelper.CreateDescriptor(
        DiagnosticId,
        "Enqueue with deadlineAfter should specify bounded maxAttempts",
        "Enqueue call specifies deadlineAfter without explicit maxAttempts; specify maxAttempts to prevent unbounded or repetitive execution within the deadline window",
        "Reliability",
        "When deadlineAfter is set, a job that repeatedly fails without an explicit attempt bound can exhaust resources or loop indefinitely until expiration. Enqueue with an explicit maxAttempts limit.");

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

        var arguments = invocation.ArgumentList.Arguments;
        var hasDeadlineAfter = false;
        var hasMaxAttempts = false;

        // Check if the invoked method signature has a maxAttempts parameter
        for (var i = 0; i < methodSymbol.Parameters.Length; i++)
        {
            if (string.Equals(methodSymbol.Parameters[i].Name, "maxAttempts", StringComparison.Ordinal))
            {
                hasMaxAttempts = true;
                break;
            }
        }

        // Check arguments for deadlineAfter and maxAttempts
        for (var i = 0; i < arguments.Count; i++)
        {
            var arg = arguments[i];
            if (arg.NameColon != null)
            {
                var name = arg.NameColon.Name.Identifier.Text;
                if (string.Equals(name, "deadlineAfter", StringComparison.Ordinal))
                {
                    hasDeadlineAfter = true;
                }
                else if (string.Equals(name, "maxAttempts", StringComparison.Ordinal))
                {
                    hasMaxAttempts = true;
                }
            }
            else
            {
                // Positional: inspect the corresponding parameter symbol
                if (i < methodSymbol.Parameters.Length)
                {
                    var param = methodSymbol.Parameters[i];
                    if (string.Equals(param.Name, "deadlineAfter", StringComparison.Ordinal))
                    {
                        hasDeadlineAfter = true;
                    }
                    else if (string.Equals(param.Name, "maxAttempts", StringComparison.Ordinal))
                    {
                        hasMaxAttempts = true;
                    }
                }
            }
        }

        if (hasDeadlineAfter && !hasMaxAttempts)
        {
            var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }
}
