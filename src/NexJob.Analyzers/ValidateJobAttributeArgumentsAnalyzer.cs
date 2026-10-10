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

        var classDeclaration = attributeSyntax.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDeclaration == null)
        {
            return;
        }

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken) as INamedTypeSymbol;
        if (!AnalyzerHelper.ImplementsIJob(classSymbol))
        {
            return;
        }

        var attributeName = attributeSyntax.Name.ToString();
        var arguments = attributeSyntax.ArgumentList?.Arguments;
        if (arguments == null || arguments.Value.Count == 0)
        {
            return;
        }

        if (attributeName.Contains("Retry"))
        {
            ValidateRetry(context, attributeSyntax, arguments.Value);
        }
        else if (attributeName.Contains("Throttle"))
        {
            ValidateThrottle(context, attributeSyntax, arguments.Value);
        }
        else if (attributeName.Contains("ExecutionTimeout"))
        {
            ValidateTimeout(context, attributeSyntax, arguments.Value);
        }
    }

    private static void ValidateRetry(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attributeSyntax,
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments)
    {
        var firstArg = arguments[0].Expression;
        var constantValue = context.SemanticModel.GetConstantValue(firstArg, context.CancellationToken);
        if (constantValue.HasValue && constantValue.Value is int attempts && attempts < 0)
        {
            var diagnostic = Diagnostic.Create(Descriptor, attributeSyntax.GetLocation(), "Retry", "attempts must be greater than or equal to 0");
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void ValidateThrottle(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attributeSyntax,
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments)
    {
        if (arguments.Count < 2)
        {
            return;
        }

        var resourceArg = arguments[0].Expression;
        var resourceConstant = context.SemanticModel.GetConstantValue(resourceArg, context.CancellationToken);
        if (resourceConstant.HasValue && resourceConstant.Value is string resource && string.IsNullOrWhiteSpace(resource))
        {
            var diagnostic = Diagnostic.Create(Descriptor, attributeSyntax.GetLocation(), "Throttle", "resource name cannot be null or whitespace");
            context.ReportDiagnostic(diagnostic);
            return;
        }

        var maxConcurrentArg = arguments[1].Expression;
        var maxConcurrentConstant = context.SemanticModel.GetConstantValue(maxConcurrentArg, context.CancellationToken);
        if (maxConcurrentConstant.HasValue && maxConcurrentConstant.Value is int maxConcurrent && maxConcurrent < 1)
        {
            var diagnostic = Diagnostic.Create(Descriptor, attributeSyntax.GetLocation(), "Throttle", "maxConcurrent must be at least 1");
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void ValidateTimeout(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attributeSyntax,
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments)
    {
        var timeoutArg = arguments[0].Expression;
        var constantValue = context.SemanticModel.GetConstantValue(timeoutArg, context.CancellationToken);
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
