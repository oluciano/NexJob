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
    public static bool IsInsideJobMethod(SyntaxNode node, SemanticModel semanticModel)
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

        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration) as INamedTypeSymbol;
        return ImplementsIJob(typeSymbol);
    }
}
