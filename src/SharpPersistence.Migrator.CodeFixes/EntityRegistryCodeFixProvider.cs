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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EntityRegistryCodeFixProvider))]
[Shared]
public sealed class EntityRegistryCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
    [
        DiagnosticIds.RegistryMustBeSealedPartialImplementingInterface,
        DiagnosticIds.ConfigurationInvalid,
        DiagnosticIds.RegistryMustDeclareConfigure,
        DiagnosticIds.EntityOrMarkerMustBePublic
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

            if (diagnostic.Id == DiagnosticIds.EntityOrMarkerMustBePublic &&
                node.FirstAncestorOrSelf<TypeDeclarationSyntax>() is { } typeDecl)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Make type public",
                        createChangedDocument: ct => MakeTypePublicAsync(context.Document, typeDecl, ct),
                        equivalenceKey: $"{DiagnosticIds.EntityOrMarkerMustBePublic}_MakeTypePublic"),
                    diagnostic);
                continue;
            }

            var classDecl = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
            if (classDecl is null)
            {
                continue;
            }

            switch (diagnostic.Id)
            {
                case DiagnosticIds.RegistryMustBeSealedPartialImplementingInterface:
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: "Make sealed partial and implement IEntityRegistry",
                            createChangedDocument: ct => FixRegistryDeclarationAsync(context.Document, classDecl, ct),
                            equivalenceKey: $"{DiagnosticIds.RegistryMustBeSealedPartialImplementingInterface}_FixRegistryDeclaration"),
                        diagnostic);
                    break;

                case DiagnosticIds.ConfigurationInvalid:
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: "Fix [EntityConfiguration] declaration",
                            createChangedDocument: ct => FixConfigurationDeclarationAsync(context.Document, classDecl, ct),
                            equivalenceKey: $"{DiagnosticIds.ConfigurationInvalid}_FixConfigurationDeclaration"),
                        diagnostic);
                    break;

                case DiagnosticIds.RegistryMustDeclareConfigure:
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: "Add Configure(IEntityRegistryBuilder registry) method",
                            createChangedDocument: ct => AddConfigureMethodAsync(context.Document, classDecl, ct),
                            equivalenceKey: $"{DiagnosticIds.RegistryMustDeclareConfigure}_AddConfigureMethod"),
                        diagnostic);
                    break;
            }
        }
    }

    private static async Task<Document> FixRegistryDeclarationAsync(
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
        updated = CodeFixSyntaxHelpers.EnsureBaseType(updated, KnownNames.Types.EntityRegistryInterface);

        var newRoot = root.ReplaceNode(classDecl, updated);
        newRoot = CodeFixSyntaxHelpers.EnsureUsing(newRoot, KnownNames.Namespaces.MigratorAbstractions);
        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> FixConfigurationDeclarationAsync(
        Document document,
        ClassDeclarationSyntax classDecl,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var updated = CodeFixSyntaxHelpers.EnsureModifiers(classDecl, makeSealed: true);

        var entityType = CodeFixSyntaxHelpers.GetGenericAttributeTypeArgument(classDecl, KnownNames.Attributes.EntityConfiguration);
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            updated = CodeFixSyntaxHelpers.EnsureBaseType(updated, $"{KnownNames.Types.EntityConfigurationInterface}<{entityType}>");
        }

        var ctors = updated.Members.OfType<ConstructorDeclarationSyntax>()
            .Where(c => !c.Modifiers.Any(SyntaxKind.StaticKeyword))
            .ToList();

        var hasPublicParameterless = ctors.Count == 0 && updated.ParameterList is null ||
            ctors.Any(c => c.ParameterList.Parameters.Count == 0 && c.Modifiers.Any(SyntaxKind.PublicKeyword));

        if (!hasPublicParameterless)
        {
            var ctor = SyntaxFactory.ConstructorDeclaration(updated.Identifier.ValueText)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .WithBody(SyntaxFactory.Block())
                .WithAdditionalAnnotations(Formatter.Annotation);

            updated = updated.WithMembers(updated.Members.Insert(0, ctor));
        }

        var newRoot = root.ReplaceNode(classDecl, updated);
        newRoot = CodeFixSyntaxHelpers.EnsureUsing(newRoot, KnownNames.Namespaces.MigratorAbstractions);
        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> AddConfigureMethodAsync(
        Document document,
        ClassDeclarationSyntax classDecl,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var method = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                KnownNames.Methods.Configure)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddParameterListParameters(
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("registry"))
                    .WithType(SyntaxFactory.ParseTypeName(KnownNames.Types.EntityRegistryBuilderInterface)))
            .WithBody(SyntaxFactory.Block())
            .WithAdditionalAnnotations(Formatter.Annotation);

        var updated = classDecl.AddMembers(method);
        var newRoot = root.ReplaceNode(classDecl, updated);
        newRoot = CodeFixSyntaxHelpers.EnsureUsing(newRoot, KnownNames.Namespaces.MigratorAbstractions);
        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> MakeTypePublicAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var typesToMakePublic = typeDecl.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().ToList();
        var newRoot = root.ReplaceNodes(
            typesToMakePublic,
            (_, current) => CodeFixSyntaxHelpers.EnsurePublic(current));

        return document.WithSyntaxRoot(newRoot);
    }
}
