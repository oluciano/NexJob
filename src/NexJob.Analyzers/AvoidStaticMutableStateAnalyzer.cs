using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that flags mutable static fields declared inside IJob classes.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidStaticMutableStateAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ005";

    private const string Title = "Avoid static mutable state in job classes";
    private const string MessageFormat = "Field '{0}' is static and mutable; job state must remain isolated across worker instances";
    private const string Description = "Background jobs run concurrently across multiple workers and nodes. Static mutable state causes race conditions and cross-job pollution.";
    private const string HelpLinkUri = "https://oluciano.github.io/NexJob/guides/best-practices.md";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reliability",
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

        context.RegisterSyntaxNodeAction(AnalyzeFieldDeclaration, SyntaxKind.FieldDeclaration);
    }

    private static void AnalyzeFieldDeclaration(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not FieldDeclarationSyntax fieldDeclaration)
        {
            return;
        }

        var isStatic = fieldDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword);
        var isReadOnly = fieldDeclaration.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) || fieldDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword);

        if (!isStatic || isReadOnly)
        {
            return;
        }

        var classDeclaration = fieldDeclaration.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDeclaration == null)
        {
            return;
        }

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
        if (!AnalyzerHelper.ImplementsIJob(classSymbol))
        {
            return;
        }

        for (var i = 0; i < fieldDeclaration.Declaration.Variables.Count; i++)
        {
            var variable = fieldDeclaration.Declaration.Variables[i];
            var diagnostic = Diagnostic.Create(Rule, variable.Identifier.GetLocation(), variable.Identifier.Text);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
