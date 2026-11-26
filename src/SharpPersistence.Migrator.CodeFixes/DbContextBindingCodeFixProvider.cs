using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using SharpPersistence.Migrator.Constants;

namespace SharpPersistence.Migrator.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DbContextBindingCodeFixProvider))]
[Shared]
public sealed class DbContextBindingCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
    [
        DiagnosticIds.DbContextMustBeSealedPartial,
        DiagnosticIds.InvalidRegistryItemCombination,
        DiagnosticIds.DuplicateDbContextBindingAttribute,
        DiagnosticIds.OrphanEntityRegistryItem,
        DiagnosticIds.DbContextMissingSchemaWiring
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

            switch (diagnostic.Id)
            {
                case DiagnosticIds.DbContextMustBeSealedPartial:
                    if (node.FirstAncestorOrSelf<ClassDeclarationSyntax>() is { } dbCtxClass)
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                title: "Make sealed partial and inherit DbContext",
                                createChangedDocument: ct => FixDbContextDeclarationAsync(context.Document, dbCtxClass, ct),
                                equivalenceKey: $"{DiagnosticIds.DbContextMustBeSealedPartial}_FixDbContextDeclaration"),
                            diagnostic);
                    }
                    break;

                case DiagnosticIds.InvalidRegistryItemCombination:
                    if (node.FirstAncestorOrSelf<AttributeSyntax>() is { } itemAttr)
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                title: "Keep Exclude = true (remove ReadOnly/Pluralize)",
                                createChangedDocument: ct => FilterItemAttributeArgsAsync(context.Document, itemAttr, keepExclude: true, ct),
                                equivalenceKey: $"{DiagnosticIds.InvalidRegistryItemCombination}_KeepExclude"),
                            diagnostic);
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                title: "Remove Exclude = true",
                                createChangedDocument: ct => FilterItemAttributeArgsAsync(context.Document, itemAttr, keepExclude: false, ct),
                                equivalenceKey: $"{DiagnosticIds.InvalidRegistryItemCombination}_RemoveExclude"),
                            diagnostic);
                    }
                    break;

                case DiagnosticIds.DuplicateDbContextBindingAttribute:
                    if (node.FirstAncestorOrSelf<AttributeSyntax>() is { } dupAttr)
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                title: "Remove duplicate attribute",
                                createChangedDocument: ct => RemoveAttributeAsync(context.Document, dupAttr, ct),
                                equivalenceKey: $"{DiagnosticIds.DuplicateDbContextBindingAttribute}_RemoveDuplicateAttribute"),
                            diagnostic);
                    }
                    break;

                case DiagnosticIds.OrphanEntityRegistryItem:
                    if (node.FirstAncestorOrSelf<AttributeSyntax>() is { } orphanAttr)
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                title: "Remove orphan [EntityRegistryItem] attribute",
                                createChangedDocument: ct => RemoveAttributeAsync(context.Document, orphanAttr, ct),
                                equivalenceKey: $"{DiagnosticIds.OrphanEntityRegistryItem}_RemoveOrphanItemAttribute"),
                            diagnostic);
                    }
                    break;

                case DiagnosticIds.DbContextMissingSchemaWiring:
                    if (node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is { Identifier.ValueText: KnownNames.Methods.OnModelCreating } omcMethod)
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                title: "Convert to partial void OnModelCreatingPartial",
                                createChangedDocument: ct => ConvertToOnModelCreatingPartialAsync(context.Document, omcMethod, ct),
                                equivalenceKey: $"{DiagnosticIds.DbContextMissingSchemaWiring}_ConvertToOnModelCreatingPartial"),
                            diagnostic);
                    }
                    else if (node.FirstAncestorOrSelf<ClassDeclarationSyntax>() is { } wiringClass)
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                title: "Add DatabaseModel parameter and field",
                                createChangedDocument: ct => AddDatabaseModelWiringAsync(context.Document, wiringClass, ct),
                                equivalenceKey: $"{DiagnosticIds.DbContextMissingSchemaWiring}_AddDatabaseModelWiring"),
                            diagnostic);
                    }
                    break;
            }
        }
    }

    private static async Task<Document> FixDbContextDeclarationAsync(
        Document document,
        ClassDeclarationSyntax classDecl,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var updated = CodeFixSyntaxHelpers.EnsureModifiers(classDecl, makeSealed: true, makePartial: true);
        updated = CodeFixSyntaxHelpers.EnsureBaseType(updated, KnownNames.Types.DbContext, insertFirst: true);

        var newRoot = root.ReplaceNode(classDecl, updated);
        newRoot = CodeFixSyntaxHelpers.EnsureUsing(newRoot, KnownNames.Namespaces.EntityFrameworkCore);
        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> FilterItemAttributeArgsAsync(
        Document document,
        AttributeSyntax attribute,
        bool keepExclude,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || attribute.ArgumentList is null)
        {
            return document;
        }

        var filtered = attribute.ArgumentList.Arguments.Where(arg =>
        {
            var name = arg.NameEquals?.Name.Identifier.ValueText;
            return keepExclude
                ? !string.Equals(name, KnownNames.AttributeProperties.ReadOnly, StringComparison.Ordinal) &&
                  !string.Equals(name, KnownNames.AttributeProperties.Pluralize, StringComparison.Ordinal)
                : !string.Equals(name, KnownNames.AttributeProperties.Exclude, StringComparison.Ordinal);
        }).ToList();

        var updatedAttr = filtered.Count == 0
            ? attribute.WithArgumentList(null)
            : attribute.WithArgumentList(SyntaxFactory.AttributeArgumentList(SyntaxFactory.SeparatedList(filtered)));

        return document.WithSyntaxRoot(root.ReplaceNode(attribute, updatedAttr.WithAdditionalAnnotations(Formatter.Annotation)));
    }

    private static async Task<Document> RemoveAttributeAsync(
        Document document,
        AttributeSyntax attribute,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        return document.WithSyntaxRoot(CodeFixSyntaxHelpers.RemoveAttribute(root, attribute));
    }

    private static async Task<Document> ConvertToOnModelCreatingPartialAsync(
        Document document,
        MethodDeclarationSyntax method,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var partialModifier = SyntaxFactory.Token(SyntaxKind.PartialKeyword)
            .WithLeadingTrivia(method.GetLeadingTrivia())
            .WithTrailingTrivia(SyntaxFactory.Space);

        var updated = method
            .WithModifiers(SyntaxFactory.TokenList(partialModifier))
            .WithIdentifier(SyntaxFactory.Identifier(KnownNames.Methods.OnModelCreatingPartial).WithTriviaFrom(method.Identifier));

        if (updated.Body is not null)
        {
            var statements = updated.Body.Statements
                .Where(stmt => stmt is not ExpressionStatementSyntax
                {
                    Expression: InvocationExpressionSyntax
                    {
                        Expression: MemberAccessExpressionSyntax
                        {
                            Expression: BaseExpressionSyntax,
                            Name.Identifier.ValueText: KnownNames.Methods.OnModelCreating
                        }
                    }
                })
                .ToList();

            updated = updated.WithBody(updated.Body.WithStatements(SyntaxFactory.List(statements)));
        }

        return document.WithSyntaxRoot(root.ReplaceNode(method, updated.WithAdditionalAnnotations(Formatter.Annotation)));
    }

    private static async Task<Document> AddDatabaseModelWiringAsync(
        Document document,
        ClassDeclarationSyntax classDecl,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var param = SyntaxFactory.Parameter(SyntaxFactory.Identifier("schema"))
            .WithType(SyntaxFactory.ParseTypeName(KnownNames.Types.DatabaseModel));

        var updated = classDecl;
        if (updated.ParameterList is not null)
        {
            updated = updated.WithParameterList(updated.ParameterList.AddParameters(param));
            var field = (FieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
                $"private readonly {KnownNames.Types.DatabaseModel} {KnownNames.Fields.SharpPersistenceSchema} = schema;")!;
            updated = updated.WithMembers(updated.Members.Insert(0, field));
        }
        else if (updated.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault(c => !c.Modifiers.Any(SyntaxKind.StaticKeyword)) is { } ctor)
        {
            var newCtor = ctor.AddParameterListParameters(param);
            if (newCtor.Body is not null)
            {
                var assign = SyntaxFactory.ParseStatement($"{KnownNames.Fields.SharpPersistenceSchema} = schema;");
                newCtor = newCtor.WithBody(newCtor.Body.AddStatements(assign));
            }

            var field = (FieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
                $"private readonly {KnownNames.Types.DatabaseModel} {KnownNames.Fields.SharpPersistenceSchema};")!;

            updated = updated.ReplaceNode(ctor, newCtor);
            updated = updated.WithMembers(updated.Members.Insert(0, field));
        }

        var newRoot = root.ReplaceNode(classDecl, updated.WithAdditionalAnnotations(Formatter.Annotation));
        newRoot = CodeFixSyntaxHelpers.EnsureUsing(newRoot, KnownNames.Namespaces.MigratorModels);
        return document.WithSyntaxRoot(newRoot);
    }
}
