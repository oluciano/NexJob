using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NexJob.Analyzers;

/// <summary>
/// Centralized helpers for analyzer symbol and syntax tree inspections.
/// </summary>
internal static class AnalyzerHelper
{
    /// <summary>
    /// Base URL for online documentation guidance.
    /// </summary>
    public const string HelpBaseUrl = "https://oluciano.github.io/NexJob/guides/best-practices/";

    /// <summary>
    /// Creates a standard DiagnosticDescriptor with default severity and online documentation link.
    /// </summary>
    public static DiagnosticDescriptor CreateDescriptor(string id, string title, string messageFormat, string category, string description)
    {
        return new DiagnosticDescriptor(
            id,
            title,
            messageFormat,
            category,
            DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: description,
            helpLinkUri: HelpBaseUrl);
    }

    /// <summary>
    /// Checks whether the given type symbol implements IJob or IJob{T}.
    /// </summary>
    public static bool ImplementsIJob(INamedTypeSymbol? typeSymbol)
    {
        if (typeSymbol == null)
        {
            return false;
        }

        return typeSymbol.AllInterfaces.Any(i =>
            string.Equals(i.ContainingNamespace?.ToDisplayString(), "NexJob", StringComparison.Ordinal) &&
            (string.Equals(i.Name, "IJob", StringComparison.Ordinal) ||
             string.Equals(i.MetadataName, "IJob`1", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Checks whether a syntax node resides within the ExecuteAsync method of an IJob implementation (class or record).
    /// </summary>
    public static bool IsInsideJobMethod(SyntaxNode node, SemanticModel semanticModel, System.Threading.CancellationToken cancellationToken)
    {
        var methodDeclaration = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (methodDeclaration == null || !string.Equals(methodDeclaration.Identifier.Text, "ExecuteAsync", StringComparison.Ordinal))
        {
            return false;
        }

        var typeDeclaration = methodDeclaration.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDeclaration == null)
        {
            return false;
        }

        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken) as INamedTypeSymbol;
        return ImplementsIJob(typeSymbol);
    }

    /// <summary>
    /// Checks whether an invocation expression calls IScheduler.EnqueueAsync or ScheduleAsync.
    /// </summary>
    public static bool IsSchedulerEnqueueOrSchedule(InvocationExpressionSyntax invocation, SemanticModel semanticModel, System.Threading.CancellationToken cancellationToken, out IMethodSymbol? methodSymbol)
    {
        methodSymbol = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
        if (methodSymbol == null)
        {
            return false;
        }

        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
        {
            return false;
        }

        var isScheduler = string.Equals(containingType.Name, "IScheduler", StringComparison.Ordinal) ||
                          containingType.AllInterfaces.Any(i => string.Equals(i.Name, "IScheduler", StringComparison.Ordinal));

        if (!isScheduler)
        {
            return false;
        }

        return methodSymbol.Name.StartsWith("Enqueue", StringComparison.Ordinal) ||
               methodSymbol.Name.StartsWith("Schedule", StringComparison.Ordinal);
    }
}
