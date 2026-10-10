using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that detects UseNexJobDashboard calls configured without authorization handlers.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DashboardAuthorizationRequiredAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ008";

    private static readonly DiagnosticDescriptor Rule = AnalyzerHelper.CreateDescriptor(
        DiagnosticId,
        "Dashboard should be protected by authorization",
        "Calling '{0}' without explicit authorization handler leaves the dashboard publicly accessible; register IDashboardAuthorizationHandler",
        "Security",
        "The NexJob dashboard provides operational metrics, queue controls, and job triggering. In non-local environments it should be protected by an IDashboardAuthorizationHandler.");

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

        var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
        if (methodSymbol == null)
        {
            return;
        }

        if (!string.Equals(methodSymbol.Name, "UseNexJobDashboard", StringComparison.Ordinal))
        {
            return;
        }

        // Check if a configure lambda argument is passed
        // e.g. app.UseNexJobDashboard() or app.UseNexJobDashboard("/path") without configure action
        var hasConfigureArgument = false;
        var arguments = invocation.ArgumentList.Arguments;
        for (var i = 0; i < arguments.Count; i++)
        {
            var arg = arguments[i];
            if (arg.NameColon != null && string.Equals(arg.NameColon.Name.Identifier.Text, "configure", StringComparison.Ordinal))
            {
                hasConfigureArgument = true;
                break;
            }

            if (arg.Expression is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            {
                hasConfigureArgument = true;
                break;
            }

            var typeInfo = context.SemanticModel.GetTypeInfo(arg.Expression, context.CancellationToken).ConvertedType;
            if (typeInfo is INamedTypeSymbol named && named.DelegateInvokeMethod != null)
            {
                hasConfigureArgument = true;
                break;
            }
        }

        if (!hasConfigureArgument)
        {
            var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation(), invocation.Expression.ToString());
            context.ReportDiagnostic(diagnostic);
        }
    }
}
