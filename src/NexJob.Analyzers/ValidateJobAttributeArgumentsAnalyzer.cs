using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that validates arguments passed to job attributes.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ValidateJobAttributeArgumentsAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ019";

    private static readonly DiagnosticDescriptor Descriptor = AnalyzerHelper.CreateDescriptor(
        DiagnosticId,
        "Invalid job attribute argument",
        "Attribute '{0}' has invalid argument: {1}",
        "Reliability",
        "Job attributes such as [Retry], [Throttle], and [ExecutionTimeout] require valid positive values; invalid arguments cause runtime job failures.");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Descriptor);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, Microsoft.CodeAnalysis.CSharp.SyntaxKind.Attribute);
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not AttributeSyntax attributeSyntax)
        {
            return;
        }

        var typeDeclaration = attributeSyntax.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDeclaration == null)
        {
            return;
        }

        var typeSymbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration, context.CancellationToken) as INamedTypeSymbol;
        if (!AnalyzerHelper.ImplementsIJob(typeSymbol))
        {
            return;
        }

        var constructorSymbol = context.SemanticModel.GetSymbolInfo(attributeSyntax, context.CancellationToken).Symbol as IMethodSymbol;
        if (constructorSymbol == null)
        {
            return;
        }

        var attributeType = constructorSymbol.ContainingType;
        if (attributeType == null || !string.Equals(attributeType.ContainingNamespace?.ToDisplayString(), "NexJob", StringComparison.Ordinal))
        {
            return;
        }

        var arguments = attributeSyntax.ArgumentList?.Arguments;
        if (arguments == null || arguments.Value.Count == 0)
        {
            return;
        }

        var attributeName = attributeType.Name;
        if (string.Equals(attributeName, "RetryAttribute", StringComparison.Ordinal))
        {
            ValidateRetry(context, attributeSyntax, arguments.Value, constructorSymbol);
        }
        else if (string.Equals(attributeName, "ThrottleAttribute", StringComparison.Ordinal))
        {
            ValidateThrottle(context, attributeSyntax, arguments.Value, constructorSymbol);
        }
        else if (string.Equals(attributeName, "ExecutionTimeoutAttribute", StringComparison.Ordinal))
        {
            ValidateTimeout(context, attributeSyntax, arguments.Value, constructorSymbol);
        }
    }

    private static ExpressionSyntax? GetArgumentExpression(
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments,
        IMethodSymbol constructorSymbol,
        string parameterName)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            var arg = arguments[i];
            if (arg.NameColon != null)
            {
                if (string.Equals(arg.NameColon.Name.Identifier.Text, parameterName, StringComparison.Ordinal))
                {
                    return arg.Expression;
                }
            }
            else if (i < constructorSymbol.Parameters.Length &&
                     string.Equals(constructorSymbol.Parameters[i].Name, parameterName, StringComparison.Ordinal))
            {
                return arg.Expression;
            }
        }

        return null;
    }

    private static void ValidateRetry(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attributeSyntax,
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments,
        IMethodSymbol constructorSymbol)
    {
        var attemptsExpr = GetArgumentExpression(arguments, constructorSymbol, "attempts");
        if (attemptsExpr == null)
        {
            return;
        }

        var constantValue = context.SemanticModel.GetConstantValue(attemptsExpr, context.CancellationToken);
        if (constantValue.HasValue && constantValue.Value is int attempts && attempts < 0)
        {
            var diagnostic = Diagnostic.Create(Descriptor, attributeSyntax.GetLocation(), "Retry", "attempts must be greater than or equal to 0");
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void ValidateThrottle(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attributeSyntax,
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments,
        IMethodSymbol constructorSymbol)
    {
        var resourceExpr = GetArgumentExpression(arguments, constructorSymbol, "resource");
        if (resourceExpr != null)
        {
            var resourceConstant = context.SemanticModel.GetConstantValue(resourceExpr, context.CancellationToken);
            if (resourceConstant.HasValue && resourceConstant.Value is string resource && string.IsNullOrWhiteSpace(resource))
            {
                var diagnostic = Diagnostic.Create(Descriptor, attributeSyntax.GetLocation(), "Throttle", "resource name cannot be null or whitespace");
                context.ReportDiagnostic(diagnostic);
                return;
            }
        }

        var maxConcurrentExpr = GetArgumentExpression(arguments, constructorSymbol, "maxConcurrent");
        if (maxConcurrentExpr != null)
        {
            var maxConcurrentConstant = context.SemanticModel.GetConstantValue(maxConcurrentExpr, context.CancellationToken);
            if (maxConcurrentConstant.HasValue && maxConcurrentConstant.Value is int maxConcurrent && maxConcurrent < 1)
            {
                var diagnostic = Diagnostic.Create(Descriptor, attributeSyntax.GetLocation(), "Throttle", "maxConcurrent must be at least 1");
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static void ValidateTimeout(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attributeSyntax,
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments,
        IMethodSymbol constructorSymbol)
    {
        var timeoutExpr = GetArgumentExpression(arguments, constructorSymbol, "timeout");
        if (timeoutExpr == null)
        {
            return;
        }

        var constantValue = context.SemanticModel.GetConstantValue(timeoutExpr, context.CancellationToken);
        if (constantValue.HasValue &&
            constantValue.Value is string timeoutStr &&
            (!TimeSpan.TryParse(timeoutStr, CultureInfo.InvariantCulture, out var parsed) || parsed <= TimeSpan.Zero))
        {
            var diagnostic = Diagnostic.Create(
                Descriptor,
                attributeSyntax.GetLocation(),
                "ExecutionTimeout",
                $"'{timeoutStr}' is not a valid positive TimeSpan");
            context.ReportDiagnostic(diagnostic);
        }
    }
}
