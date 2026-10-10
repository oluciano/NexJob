using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that flags DateTime.Now usage inside background jobs.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidDateTimeNowAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ002";

    private const string Title = "Avoid DateTime.Now in job execution";
    private const string MessageFormat = "Avoid '{0}' in background jobs; use DateTime.UtcNow or context timestamp instead";
    private const string Description = "Background jobs should use UtcNow to avoid timezone drift and DST issues across clusters.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reliability",
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

        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var memberName = memberAccess.Name.Identifier.Text;
        if (!string.Equals(memberName, "Now", StringComparison.Ordinal) &&
            !string.Equals(memberName, "Today", StringComparison.Ordinal))
        {
            return;
        }

        if (!AnalyzerHelper.IsInsideJobMethod(memberAccess, context.SemanticModel))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(memberAccess).Symbol as IPropertySymbol;
        if (symbol == null || symbol.ContainingType == null)
        {
            return;
        }

        var containingType = symbol.ContainingType.ToDisplayString();
        if (string.Equals(containingType, "System.DateTime", StringComparison.Ordinal))
        {
            var diagnostic = Diagnostic.Create(Rule, memberAccess.GetLocation(), $"DateTime.{memberName}");
            context.ReportDiagnostic(diagnostic);
        }
    }
}
