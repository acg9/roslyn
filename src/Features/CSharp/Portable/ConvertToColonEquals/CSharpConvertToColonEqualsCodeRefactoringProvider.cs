// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.CodeAnalysis.CSharp.ConvertToColonEquals;

/// <summary>
/// Powerstone: refactor <c>var x = expr;</c> into the equivalent <c>x := expr;</c>
/// short declaration. Works for statement-level locals and the for-init form
/// <c>for (var i = 0; ...)</c> → <c>for (i := 0; ...)</c>.
///
/// Produces the same synthetic-token shape the parser builds when it sees
/// <c>:=</c> directly: a zero-width <c>var</c> carrying the original leading
/// trivia, plus an <c>=</c> token whose Text is <c>":="</c>.
/// </summary>
[ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = PredefinedCodeRefactoringProviderNames.ConvertToColonEquals), Shared]
internal sealed class CSharpConvertToColonEqualsCodeRefactoringProvider : CodeRefactoringProvider
{
    [ImportingConstructor]
    [SuppressMessage("RoslynDiagnosticsReliability", "RS0033:Importing constructor should be [Obsolete]", Justification = "Used in test code: https://github.com/dotnet/roslyn/issues/42814")]
    public CSharpConvertToColonEqualsCodeRefactoringProvider()
    {
    }

    public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
    {
        var declaration = await FindDeclarationAsync(context).ConfigureAwait(false);
        if (declaration is null || !CanConvert(declaration))
            return;

        context.RegisterRefactoring(
            CodeAction.Create(
                CSharpFeaturesResources.Convert_to_short_declaration,
                cancellationToken => ConvertAsync(context.Document, declaration, cancellationToken),
                nameof(CSharpFeaturesResources.Convert_to_short_declaration)));
    }

    // The refactoring-helpers' default extraction rules cover statement-level
    // VariableDeclarations but not the for-init case. Try the obvious paths
    // (the declaration itself, the `var` TypeSyntax, the declarator) and fall back
    // to the declaration ancestor for cursor positions inside the for-init.
    private static async Task<VariableDeclarationSyntax?> FindDeclarationAsync(CodeRefactoringContext context)
    {
        var declaration = await context.TryGetRelevantNodeAsync<VariableDeclarationSyntax>().ConfigureAwait(false);
        if (declaration is not null)
            return declaration;

        var type = await context.TryGetRelevantNodeAsync<TypeSyntax>().ConfigureAwait(false);
        if (type?.Parent is VariableDeclarationSyntax fromType)
            return fromType;

        var declarator = await context.TryGetRelevantNodeAsync<VariableDeclaratorSyntax>().ConfigureAwait(false);
        if (declarator?.Parent is VariableDeclarationSyntax fromDeclarator)
            return fromDeclarator;

        return null;
    }

    private static bool CanConvert(VariableDeclarationSyntax decl)
    {
        // Implicit-var only; skip already-converted (zero-width var) trees.
        if (decl.Type is not IdentifierNameSyntax { Identifier: var typeIdentifier })
            return false;
        if (!decl.Type.IsVar || typeIdentifier.Text.Length == 0)
            return false;

        // `:=` is single-declarator only.
        if (decl.Variables.Count != 1)
            return false;

        var declarator = decl.Variables[0];
        if (declarator.ArgumentList is not null)
            return false;
        if (declarator.Initializer is not { } initializer)
            return false;
        if (initializer.EqualsToken.Text == ":=")
            return false;

        // Statement-position locals (no `using`, `await using`, modifiers) and for-init only.
        return decl.Parent switch
        {
            LocalDeclarationStatementSyntax local =>
                local.Modifiers.Count == 0
                && local.UsingKeyword.IsKind(SyntaxKind.None)
                && local.AwaitKeyword.IsKind(SyntaxKind.None),
            ForStatementSyntax => true,
            _ => false,
        };
    }

    private static async Task<Document> ConvertAsync(Document document, VariableDeclarationSyntax oldDecl, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var newDecl = BuildColonEqualsDeclaration(oldDecl);
        return document.WithSyntaxRoot(root!.ReplaceNode(oldDecl, newDecl));
    }

    private static VariableDeclarationSyntax BuildColonEqualsDeclaration(VariableDeclarationSyntax oldDecl)
    {
        var varToken = ((IdentifierNameSyntax)oldDecl.Type).Identifier;
        var declarator = oldDecl.Variables[0];
        var nameToken = declarator.Identifier;
        var initializer = declarator.Initializer!;
        var oldEquals = initializer.EqualsToken;

        // Zero-width 'var' that inherits the original 'var' token's leading trivia.
        // The trailing trivia (the space between `var` and the name) is dropped because
        // the name now sits where `var` used to.
        var newVarToken = SyntaxFactory.Identifier(
            leading: varToken.LeadingTrivia,
            contextualKind: SyntaxKind.VarKeyword,
            text: "",
            valueText: "var",
            trailing: default);
        var newVarType = SyntaxFactory.IdentifierName(newVarToken);

        // Preserve the name (including @-verbatim form) and its trailing trivia.
        var newName = SyntaxFactory.Identifier(
            leading: nameToken.LeadingTrivia,
            contextualKind: SyntaxKind.IdentifierToken,
            text: nameToken.Text,
            valueText: nameToken.ValueText,
            trailing: nameToken.TrailingTrivia);

        // '=' with Text ':=' so ToFullString reproduces the converted source.
        var newEquals = SyntaxFactory.Token(
            leading: oldEquals.LeadingTrivia,
            kind: SyntaxKind.EqualsToken,
            text: ":=",
            valueText: "=",
            trailing: oldEquals.TrailingTrivia);

        var newDeclarator = declarator
            .WithIdentifier(newName)
            .WithInitializer(initializer.WithEqualsToken(newEquals));

        return oldDecl
            .WithType(newVarType)
            .WithVariables(SyntaxFactory.SingletonSeparatedList(newDeclarator));
    }
}
