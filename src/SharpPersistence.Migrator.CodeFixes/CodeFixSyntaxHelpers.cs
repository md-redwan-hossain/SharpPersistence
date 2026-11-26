using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace SharpPersistence.Migrator.CodeFixes;

internal static class CodeFixSyntaxHelpers
{
    public static ClassDeclarationSyntax EnsureModifiers(
        ClassDeclarationSyntax classDecl,
        bool makeSealed = false,
        bool makePartial = false,
        bool makePublic = false)
    {
        var existing = classDecl.Modifiers.ToList();
        var leadingTrivia = classDecl.GetLeadingTrivia();
        var hasLeading = classDecl.Modifiers.Count > 0;

        if (makePublic)
        {
            existing.RemoveAll(m =>
                m.IsKind(SyntaxKind.PrivateKeyword) ||
                m.IsKind(SyntaxKind.ProtectedKeyword) ||
                m.IsKind(SyntaxKind.InternalKeyword) ||
                m.IsKind(SyntaxKind.FileKeyword));

            if (!existing.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
            {
                existing.Insert(0, SyntaxFactory.Token(SyntaxKind.PublicKeyword));
            }
        }

        if (makeSealed)
        {
            existing.RemoveAll(m => m.IsKind(SyntaxKind.AbstractKeyword) || m.IsKind(SyntaxKind.StaticKeyword));
            if (!existing.Any(m => m.IsKind(SyntaxKind.SealedKeyword)))
            {
                var partialIdx = existing.FindIndex(m => m.IsKind(SyntaxKind.PartialKeyword));
                var token = SyntaxFactory.Token(SyntaxKind.SealedKeyword);
                if (partialIdx >= 0)
                    existing.Insert(partialIdx, token);
                else
                    existing.Add(token);
            }
        }

        if (makePartial && !existing.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
        {
            existing.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword));
        }

        var normalized = existing
            .Select(t => SyntaxFactory.Token(t.Kind()).WithTrailingTrivia(SyntaxFactory.Space))
            .ToList();

        if (normalized.Count > 0 && hasLeading)
        {
            normalized[0] = normalized[0].WithLeadingTrivia(leadingTrivia);
        }

        var updated = classDecl.WithModifiers(SyntaxFactory.TokenList(normalized));
        if (!hasLeading && normalized.Count > 0)
        {
            updated = updated
                .WithKeyword(updated.Keyword.WithLeadingTrivia(SyntaxTriviaList.Empty))
                .WithLeadingTrivia(leadingTrivia);
        }

        return updated.WithAdditionalAnnotations(Formatter.Annotation);
    }

    public static TypeDeclarationSyntax EnsurePublic(TypeDeclarationSyntax typeDecl)
    {
        var existing = typeDecl.Modifiers.ToList();
        var leadingTrivia = typeDecl.GetLeadingTrivia();
        var hasLeading = typeDecl.Modifiers.Count > 0;

        existing.RemoveAll(m =>
            m.IsKind(SyntaxKind.PrivateKeyword) ||
            m.IsKind(SyntaxKind.ProtectedKeyword) ||
            m.IsKind(SyntaxKind.InternalKeyword) ||
            m.IsKind(SyntaxKind.FileKeyword));

        if (!existing.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
        {
            existing.Insert(0, SyntaxFactory.Token(SyntaxKind.PublicKeyword));
        }

        var normalized = existing
            .Select(t => SyntaxFactory.Token(t.Kind()).WithTrailingTrivia(SyntaxFactory.Space))
            .ToList();

        if (normalized.Count > 0 && hasLeading)
        {
            normalized[0] = normalized[0].WithLeadingTrivia(leadingTrivia);
        }

        var updated = typeDecl.WithModifiers(SyntaxFactory.TokenList(normalized));
        if (!hasLeading && normalized.Count > 0)
        {
            updated = updated
                .WithKeyword(updated.Keyword.WithLeadingTrivia(SyntaxTriviaList.Empty))
                .WithLeadingTrivia(leadingTrivia);
        }

        return updated.WithAdditionalAnnotations(Formatter.Annotation);
    }

    public static ClassDeclarationSyntax EnsureBaseType(
        ClassDeclarationSyntax classDecl,
        string baseTypeName,
        bool insertFirst = false)
    {
        var baseList = classDecl.BaseList;
        var simpleTarget = StripGenericArgs(baseTypeName);

        if (baseList is not null)
        {
            foreach (var bt in baseList.Types)
            {
                var existing = StripGenericArgs(bt.Type.ToString());
                if (existing == simpleTarget || existing.EndsWith("." + simpleTarget, StringComparison.Ordinal))
                {
                    return classDecl;
                }
            }

            var newBase = SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(baseTypeName));
            var types = insertFirst
                ? baseList.Types.Insert(0, newBase)
                : baseList.Types.Add(newBase);

            return classDecl
                .WithBaseList(baseList.WithTypes(types))
                .WithAdditionalAnnotations(Formatter.Annotation);
        }

        var createdBaseList = SyntaxFactory.BaseList(
            SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(baseTypeName))));

        return classDecl
            .WithIdentifier(classDecl.Identifier.WithTrailingTrivia(SyntaxTriviaList.Empty))
            .WithBaseList(createdBaseList.WithLeadingTrivia(SyntaxFactory.Space))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    public static SyntaxNode EnsureUsing(SyntaxNode root, string namespaceName)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
        {
            return root;
        }

        if (compilationUnit.Usings.Any(u => string.Equals(u.Name?.ToString(), namespaceName, StringComparison.Ordinal)))
        {
            return root;
        }

        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return compilationUnit.AddUsings(usingDirective);
    }

    public static SyntaxNode RemoveAttribute(SyntaxNode root, AttributeSyntax attribute)
    {
        if (attribute.Parent is not AttributeListSyntax attrList)
        {
            return root;
        }

        if (attrList.Attributes.Count == 1)
        {
            return root.RemoveNode(attrList, SyntaxRemoveOptions.KeepLeadingTrivia) ?? root;
        }

        var updatedList = attrList.WithAttributes(attrList.Attributes.Remove(attribute));
        return root.ReplaceNode(attrList, updatedList);
    }

    public static string? GetGenericAttributeTypeArgument(
        ClassDeclarationSyntax classDecl,
        string attributeSimpleName)
    {
        foreach (var list in classDecl.AttributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                if (attr.Name is GenericNameSyntax g &&
                    MatchesAttributeName(g.Identifier.ValueText, attributeSimpleName) &&
                    g.TypeArgumentList.Arguments.Count == 1)
                {
                    return g.TypeArgumentList.Arguments[0].ToString();
                }

                if (attr.Name is QualifiedNameSyntax { Right: GenericNameSyntax qg } &&
                    MatchesAttributeName(qg.Identifier.ValueText, attributeSimpleName) &&
                    qg.TypeArgumentList.Arguments.Count == 1)
                {
                    return qg.TypeArgumentList.Arguments[0].ToString();
                }
            }
        }

        return null;
    }

    private static bool MatchesAttributeName(string actual, string expectedSimple) =>
        string.Equals(actual, expectedSimple, StringComparison.Ordinal) ||
        string.Equals(actual, expectedSimple + "Attribute", StringComparison.Ordinal);

    private static string StripGenericArgs(string typeName)
    {
        var idx = typeName.IndexOf('<');
        return idx >= 0 ? typeName.Substring(0, idx).Trim() : typeName.Trim();
    }
}
