using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that validates job class/record accessibility and constructor declarations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JobClassStructureAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ003";

    private const string Title = "Job class must be public and instantiable";
    private const string MessageFormat = "Job '{0}' must be a public, non-abstract class or record with a public constructor";
    private const string Description = "NexJob instantiates jobs via dependency injection, which requires public non-abstract classes or records.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Design",
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

        context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration, SyntaxKind.ClassDeclaration, SyntaxKind.RecordDeclaration);
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not TypeDeclarationSyntax typeDeclaration)
        {
            return;
        }

        var typeSymbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration);
        if (typeSymbol == null || !AnalyzerHelper.ImplementsIJob(typeSymbol))
        {
            return;
        }

        var isPublic = typeSymbol.DeclaredAccessibility == Accessibility.Public;
        var isAbstract = typeSymbol.IsAbstract;

        var constructors = typeSymbol.InstanceConstructors;
        var hasPublicConstructor = constructors.IsEmpty || constructors.Any(c => c.DeclaredAccessibility == Accessibility.Public);

        if (!isPublic || isAbstract || !hasPublicConstructor)
        {
            var diagnostic = Diagnostic.Create(Rule, typeDeclaration.Identifier.GetLocation(), typeDeclaration.Identifier.Text);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
