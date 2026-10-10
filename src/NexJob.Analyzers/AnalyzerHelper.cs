using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace NexJob.Analyzers;

/// <summary>
/// Internal helpers for analyzer symbol inspection.
/// </summary>
internal static class AnalyzerHelper
{
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
             string.Equals(i.Name, "IJob`1", StringComparison.Ordinal) ||
             string.Equals(i.MetadataName, "IJob`1", StringComparison.Ordinal)));
    }
}
