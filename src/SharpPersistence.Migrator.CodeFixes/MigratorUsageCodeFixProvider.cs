using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using SharpPersistence.Migrator.Constants;

namespace SharpPersistence.Migrator.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MigratorUsageCodeFixProvider))]
[Shared]
public sealed class MigratorUsageCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
    [
        DiagnosticIds.RequiredColumnInsideOptionalOwnedNavigation,
        DiagnosticIds.InvalidEntityStubMapping
    ];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (node.FirstAncestorOrSelf<InvocationExpressionSyntax>() is not { } invocation)
            {
                continue;
            }

            var (title, key, removeStmt) = diagnostic.Id == DiagnosticIds.RequiredColumnInsideOptionalOwnedNavigation
                ? ("Remove .IsRequired() call", $"{DiagnosticIds.RequiredColumnInsideOptionalOwnedNavigation}_RemoveIsRequired", false)
                : ("Remove invalid stub mapping", $"{DiagnosticIds.InvalidEntityStubMapping}_RemoveInvalidStubMapping", true);

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: ct => RemoveChainedInvocationAsync(context.Document, invocation, removeStmt, ct),
                    equivalenceKey: key),
                diagnostic);
        }
    }

    private static async Task<Document> RemoveChainedInvocationAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        bool removeWholeStatementIfRoot,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return document;
        }

        if (removeWholeStatementIfRoot &&
            invocation.Parent is ExpressionStatementSyntax stmt &&
            memberAccess.Expression is not InvocationExpressionSyntax)
        {
            var removedRoot = root.RemoveNode(stmt, SyntaxRemoveOptions.KeepNoTrivia) ?? root;
            return document.WithSyntaxRoot(removedRoot);
        }

        var replacement = memberAccess.Expression
            .WithTriviaFrom(invocation)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, replacement));
    }
}
