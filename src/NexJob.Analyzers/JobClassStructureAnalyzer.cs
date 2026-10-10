using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that validates job class accessibility and constructor declarations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JobClassStructureAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ003";

    private const string Title = "Job class must be public and instantiable";
    private const string MessageFormat = "Job '{0}' must be a public, non-abstract class with a public constructor";
    private const string Description = "NexJob instantiates jobs via dependency injection, which requires public non-abstract classes.";
    private const string HelpLinkUri = "https://oluciano.github.io/NexJob/guides/best-practices.md";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Design",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: HelpLinkUri);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeClassDeclaration, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclaration)
        {
            return;
        }

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
        if (classSymbol == null || !AnalyzerHelper.ImplementsIJob(classSymbol))
        {
            return;
        }

        var isPublic = classSymbol.DeclaredAccessibility == Accessibility.Public;
        var isAbstract = classSymbol.IsAbstract;

        var constructors = classSymbol.InstanceConstructors;
        var hasPublicConstructor = constructors.IsEmpty || constructors.Any(c => c.DeclaredAccessibility == Accessibility.Public);

        if (!isPublic || isAbstract || !hasPublicConstructor)
        {
            var diagnostic = Diagnostic.Create(Rule, classDeclaration.Identifier.GetLocation(), classDeclaration.Identifier.Text);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
