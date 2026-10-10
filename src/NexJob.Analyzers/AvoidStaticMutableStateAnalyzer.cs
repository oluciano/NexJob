using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that flags mutable static fields and properties inside IJob classes/records.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidStaticMutableStateAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ005";

    private const string Title = "Avoid static mutable state in job classes";
    private const string MessageFormat = "Member '{0}' is static and mutable; job state must remain isolated across worker instances";
    private const string Description = "Background jobs run concurrently across multiple workers and nodes. Static mutable state causes race conditions and cross-job pollution.";

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

        context.RegisterSyntaxNodeAction(AnalyzeFieldDeclaration, SyntaxKind.FieldDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzePropertyDeclaration, SyntaxKind.PropertyDeclaration);
    }

    private static void AnalyzeFieldDeclaration(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not FieldDeclarationSyntax fieldDeclaration)
        {
            return;
        }

        var isStatic = fieldDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword);
        var isReadOnly = fieldDeclaration.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) || fieldDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword);

        if (!isStatic)
        {
            return;
        }

        var typeDeclaration = fieldDeclaration.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDeclaration == null)
        {
            return;
        }

        var typeSymbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration);
        if (!AnalyzerHelper.ImplementsIJob(typeSymbol))
        {
            return;
        }

        var typeInfo = context.SemanticModel.GetTypeInfo(fieldDeclaration.Declaration.Type).Type;
        var isMutableCollection = typeInfo != null && IsMutableCollectionType(typeInfo.ToDisplayString());

        // Flag if not readonly OR if it is a mutable collection (even if readonly, contents are mutable)
        if (!isReadOnly || isMutableCollection)
        {
            for (var i = 0; i < fieldDeclaration.Declaration.Variables.Count; i++)
            {
                var variable = fieldDeclaration.Declaration.Variables[i];
                var diagnostic = Diagnostic.Create(Rule, variable.Identifier.GetLocation(), variable.Identifier.Text);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static void AnalyzePropertyDeclaration(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not PropertyDeclarationSyntax propertyDeclaration)
        {
            return;
        }

        var propertySymbol = context.SemanticModel.GetDeclaredSymbol(propertyDeclaration);
        if (propertySymbol == null || !propertySymbol.IsStatic)
        {
            return;
        }

        var hasSetter = propertySymbol.SetMethod != null;
        var isMutableCollection = propertySymbol.Type != null && IsMutableCollectionType(propertySymbol.Type.ToDisplayString());

        if (!hasSetter && !isMutableCollection)
        {
            return;
        }

        var typeDeclaration = propertyDeclaration.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDeclaration == null)
        {
            return;
        }

        var typeSymbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration);
        if (!AnalyzerHelper.ImplementsIJob(typeSymbol))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(Rule, propertyDeclaration.Identifier.GetLocation(), propertyDeclaration.Identifier.Text);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsMutableCollectionType(string typeDisplayString)
    {
        return typeDisplayString.StartsWith("System.Collections.Generic.List<", StringComparison.Ordinal) ||
               typeDisplayString.StartsWith("System.Collections.Generic.Dictionary<", StringComparison.Ordinal) ||
               typeDisplayString.StartsWith("System.Collections.Generic.HashSet<", StringComparison.Ordinal) ||
               typeDisplayString.StartsWith("System.Collections.Generic.Queue<", StringComparison.Ordinal) ||
               typeDisplayString.StartsWith("System.Collections.Generic.Stack<", StringComparison.Ordinal);
    }
}
