using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

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
    /// Creates a standard DiagnosticDescriptor with configurable severity and online documentation link.
    /// </summary>
    public static DiagnosticDescriptor CreateDescriptor(
        string id,
        string title,
        string messageFormat,
        string category,
        string description,
        bool isEnabledByDefault = true)
    {
        return new DiagnosticDescriptor(
            id,
            title,
            messageFormat,
            category,
            DiagnosticSeverity.Info,
            isEnabledByDefault: isEnabledByDefault,
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
    /// Overloads accepting a pre-built JobRecord are excluded because their queue/idempotency configuration lives on the record.
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

        var isEnqueueOrSchedule = methodSymbol.Name.StartsWith("Enqueue", StringComparison.Ordinal) ||
                                  methodSymbol.Name.StartsWith("Schedule", StringComparison.Ordinal);

        if (!isEnqueueOrSchedule)
        {
            return false;
        }

        if (methodSymbol.Parameters.Length > 0 &&
            string.Equals(methodSymbol.Parameters[0].Type.Name, "JobRecord", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Registers an invocation syntax node action.
    /// </summary>
    public static void RegisterInvocation(AnalysisContext context, Action<SyntaxNodeAnalysisContext> action)
    {
        context.RegisterSyntaxNodeAction(action, Microsoft.CodeAnalysis.CSharp.SyntaxKind.InvocationExpression);
    }

    /// <summary>
    /// Checks whether the method symbol declares a parameter with the specified name.
    /// </summary>
    public static bool HasParameter(IMethodSymbol methodSymbol, string parameterName)
    {
        return methodSymbol.Parameters.Any(p => string.Equals(p.Name, parameterName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Checks whether an argument list contains an argument by name or at its positional index.
    /// </summary>
    public static bool HasNamedOrPositionalArgument(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol methodSymbol,
        string parameterName)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            var arg = arguments[i];
            if (arg.NameColon != null)
            {
                if (string.Equals(arg.NameColon.Name.Identifier.Text, parameterName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (i < methodSymbol.Parameters.Length &&
                     string.Equals(methodSymbol.Parameters[i].Name, parameterName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
