using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpPersistence.Migrator.Cli.Translators;

internal sealed record ResolvedMarkerAttribute(
    string AttributeShortName,
    string AttributeFullName,
    string? AttributeNamespace,
    string? SourceFilePath,
    string? Warning,
    string? GroupNamespace = null);

internal sealed record AnnotatedEntityItem(
    string EntityName,
    string FilePath,
    string RelativePath,
    bool AlreadyAnnotated,
    bool Modified);

internal sealed record EntityAnnotationResult(
    ResolvedMarkerAttribute MarkerAttribute,
    IReadOnlyList<string> DiscoveredEntityNames,
    IReadOnlyList<AnnotatedEntityItem> Items,
    IReadOnlyList<string> UnmatchedEntityNames,
    IReadOnlyList<string> Warnings);

internal static class EfCoreEntityAnnotator
{
    public const string DefaultAttributeName = "Entity";
    public const string DefaultAttributeNamespace = "SharpPersistence.Abstractions.Attributes";

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git",
        "node_modules"
    };

    public static ResolvedMarkerAttribute ResolveMarkerAttribute(
        string? entityGroupName,
        string? entityGroupNamespace = null)
    {
        var hasName = !string.IsNullOrWhiteSpace(entityGroupName);
        var hasNamespace = !string.IsNullOrWhiteSpace(entityGroupNamespace);
        if (hasName != hasNamespace)
        {
            throw new ArgumentException("Both entity group name and entity group namespace must be provided together.");
        }

        if (!hasName)
        {
            return new ResolvedMarkerAttribute(
                DefaultAttributeName,
                "EntityAttribute",
                DefaultAttributeNamespace,
                null,
                null);
        }

        var trimmedName = entityGroupName!.Trim();
        var trimmedNs = entityGroupNamespace!.Trim();

        return new ResolvedMarkerAttribute(
            $"{DefaultAttributeName}<{trimmedName}>",
            $"EntityAttribute<{trimmedName}>",
            DefaultAttributeNamespace,
            null,
            null,
            trimmedNs);
    }

    public static ResolvedMarkerAttribute ParseMarkerAttributeSource(string sourceCode, string? sourceFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);

        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = tree.GetCompilationUnitRoot();

        var groupType = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => (t is StructDeclarationSyntax or RecordDeclarationSyntax) && ImplementsEntityGroup(t));

        if (groupType is not null)
        {
            var groupName = groupType.Identifier.ValueText;
            var groupNs = GetDeclaredNamespace(groupType);
            return new ResolvedMarkerAttribute(
                $"{DefaultAttributeName}<{groupName}>",
                $"EntityAttribute<{groupName}>",
                DefaultAttributeNamespace,
                sourceFilePath,
                null,
                groupNs);
        }

        var classes = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(c => !c.Modifiers.Any(SyntaxKind.AbstractKeyword) && !c.Modifiers.Any(SyntaxKind.StaticKeyword))
            .ToList();

        if (classes.Count == 0)
        {
            throw new InvalidOperationException(
                $"No IEntityGroup struct or attribute class found in '{sourceFilePath ?? "attribute source"}'.");
        }

        var attrClass = classes.FirstOrDefault(c => c.Identifier.ValueText.EndsWith("Attribute", StringComparison.Ordinal))
                        ?? classes[0];

        var rawName = attrClass.Identifier.ValueText;
        var shortName = StripAttributeSuffix(rawName);
        var ns = GetDeclaredNamespace(attrClass);
        return new ResolvedMarkerAttribute(shortName, rawName, ns, sourceFilePath, null);
    }

    public static (HashSet<string> EntityNames, HashSet<string> ImportedNamespaces) ExtractDbContextEntities(
        string dbContextPath)
    {
        var fullPath = PathGuard.EnsureReadableFileOrDirectory(dbContextPath, "--dbcontext");
        IEnumerable<string> files = File.Exists(fullPath)
            ? [fullPath]
            : EnumerateCsFiles(fullPath);

        var entityNames = new HashSet<string>(StringComparer.Ordinal);
        var importedNamespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            ExtractDbContextEntitiesFromSource(source, entityNames, importedNamespaces);
        }

        return (entityNames, importedNamespaces);
    }

    public static void ExtractDbContextEntitiesFromSource(
        string sourceCode,
        HashSet<string> entityNames,
        HashSet<string> importedNamespaces)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = tree.GetCompilationUnitRoot();

        foreach (var usingDir in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            if (usingDir.StaticKeyword.IsKind(SyntaxKind.None) &&
                usingDir.Alias is null &&
                usingDir.Name is not null)
            {
                importedNamespaces.Add(usingDir.Name.ToString());
            }
        }

        foreach (var nsDecl in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
        {
            importedNamespaces.Add(nsDecl.Name.ToString());
        }

        // 1. DbSet<TEntity> properties
        foreach (var prop in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            if (TryGetGenericTypeArgumentName(prop.Type, "DbSet", out var entityName))
            {
                entityNames.Add(entityName);
            }
        }

        // 2. modelBuilder.Entity<TEntity>(...) invocations
        foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var genericName = inv.Expression switch
            {
                MemberAccessExpressionSyntax { Name: GenericNameSyntax g } => g,
                GenericNameSyntax g => g,
                _ => null
            };

            if (genericName is not null &&
                string.Equals(genericName.Identifier.ValueText, "Entity", StringComparison.Ordinal) &&
                genericName.TypeArgumentList.Arguments.Count == 1)
            {
                var entityName = GetSimpleTypeName(genericName.TypeArgumentList.Arguments[0]);
                if (!string.IsNullOrWhiteSpace(entityName))
                {
                    entityNames.Add(entityName);
                }
            }
        }
    }

    public static EntityAnnotationResult AnnotateDirectory(
        string dbContextPath,
        string searchDirectory,
        string? entityGroupName = null,
        string? entityGroupNamespace = null,
        bool readOnly = false,
        bool dryRun = false)
    {
        var fullSearchDir = PathGuard.EnsureReadableDirectory(searchDirectory, "--search-directory");
        if (!dryRun)
        {
            PathGuard.EnsureWritableDirectory(fullSearchDir, "--search-directory", dryRun: false);
        }

        var marker = ResolveMarkerAttribute(entityGroupName, entityGroupNamespace);
        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(marker.Warning))
        {
            warnings.Add(marker.Warning);
        }

        var (entityNames, importedNamespaces) = ExtractDbContextEntities(dbContextPath);
        var sortedEntityNames = entityNames.OrderBy(n => n, StringComparer.Ordinal).ToList();

        if (sortedEntityNames.Count == 0)
        {
            warnings.Add($"No DbSet<T> properties or modelBuilder.Entity<T>() calls found in '{dbContextPath}'.");
            return new EntityAnnotationResult(marker, sortedEntityNames, [], [], warnings);
        }

        var allCsFiles = EnumerateCsFiles(fullSearchDir).ToList();
        var items = new List<AnnotatedEntityItem>();
        var matchedEntities = new HashSet<string>(StringComparer.Ordinal);
        var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pass 1: Fast filename match (e.g. User.cs -> User)
        foreach (var file in allCsFiles)
        {
            var fileStem = Path.GetFileNameWithoutExtension(file);
            if (!entityNames.Contains(fileStem))
            {
                continue;
            }

            processedFiles.Add(file);
            ProcessCandidateFile(
                file,
                fullSearchDir,
                entityNames,
                importedNamespaces,
                marker,
                readOnly,
                dryRun,
                items,
                matchedEntities);
        }

        // Pass 2: Fallback scan only for entities not matched by filename (e.g. multi-class files)
        var remaining = entityNames.Where(n => !matchedEntities.Contains(n)).ToHashSet(StringComparer.Ordinal);
        if (remaining.Count > 0)
        {
            foreach (var file in allCsFiles)
            {
                if (processedFiles.Contains(file))
                {
                    continue;
                }

                var source = File.ReadAllText(file);
                if (!ContainsAnyIdentifier(source, remaining))
                {
                    continue;
                }

                ProcessCandidateFile(
                    file,
                    fullSearchDir,
                    remaining,
                    importedNamespaces,
                    marker,
                    readOnly,
                    dryRun,
                    items,
                    matchedEntities,
                    preloadedSource: source);
            }
        }

        var unmatched = sortedEntityNames
            .Where(n => !matchedEntities.Contains(n))
            .ToList();

        return new EntityAnnotationResult(marker, sortedEntityNames, items, unmatched, warnings);
    }

    public static (string RewrittenSource, IReadOnlyList<(string EntityName, bool AlreadyAnnotated, bool Modified)> Changes)
        AnnotateSource(
            string sourceCode,
            ISet<string> targetEntityNames,
            ResolvedMarkerAttribute marker,
            bool readOnly = false,
            ISet<string>? preferredNamespaces = null)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);

        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = tree.GetCompilationUnitRoot();
        var newline = DetectNewLine(sourceCode);

        var rewriter = new EntityAnnotationSyntaxRewriter(
            targetEntityNames,
            marker,
            readOnly,
            preferredNamespaces,
            newline);

        var rewrittenRoot = (CompilationUnitSyntax)rewriter.Visit(root);
        if (rewriter.AnyModified)
        {
            if (!string.IsNullOrWhiteSpace(marker.AttributeNamespace))
            {
                rewrittenRoot = EnsureUsingDirective(rewrittenRoot, marker.AttributeNamespace, newline);
            }

            if (!string.IsNullOrWhiteSpace(marker.GroupNamespace) &&
                !string.Equals(marker.GroupNamespace, marker.AttributeNamespace, StringComparison.Ordinal))
            {
                rewrittenRoot = EnsureUsingDirective(rewrittenRoot, marker.GroupNamespace, newline);
            }
        }

        return (rewrittenRoot.ToFullString(), rewriter.Changes);
    }

    private static void ProcessCandidateFile(
        string filePath,
        string searchRoot,
        ISet<string> targetEntityNames,
        ISet<string> importedNamespaces,
        ResolvedMarkerAttribute marker,
        bool readOnly,
        bool dryRun,
        List<AnnotatedEntityItem> items,
        HashSet<string> matchedEntities,
        string? preloadedSource = null)
    {
        var source = preloadedSource ?? File.ReadAllText(filePath);
        var (rewritten, changes) = AnnotateSource(source, targetEntityNames, marker, readOnly, preferredNamespaces: null);
        if (changes.Count == 0)
        {
            return;
        }

        var relative = Path.GetRelativePath(searchRoot, filePath);
        var fileModified = false;

        foreach (var (entityName, alreadyAnnotated, modified) in changes)
        {
            matchedEntities.Add(entityName);
            items.Add(new AnnotatedEntityItem(entityName, filePath, relative, alreadyAnnotated, modified));
            if (modified)
            {
                fileModified = true;
            }
        }

        if (fileModified && !dryRun)
        {
            File.WriteAllText(filePath, rewritten, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static IEnumerable<string> EnumerateCsFiles(string rootDir)
    {
        var stack = new Stack<string>();
        stack.Push(rootDir);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var subDir in Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(subDir);
                if (!SkippedDirectories.Contains(name))
                {
                    stack.Push(subDir);
                }
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static bool ContainsAnyIdentifier(string source, ISet<string> names)
    {
        foreach (var name in names)
        {
            if (source.Contains(name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsEntityGroup(TypeDeclarationSyntax typeDecl)
    {
        if (typeDecl.BaseList is null)
        {
            return false;
        }

        foreach (var baseType in typeDecl.BaseList.Types)
        {
            var name = GetSimpleTypeName(baseType.Type);
            if (string.Equals(name, "IEntityGroup", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetDeclaredNamespace(SyntaxNode node)
    {
        var nsParts = new List<string>();
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is BaseNamespaceDeclarationSyntax ns)
            {
                nsParts.Add(ns.Name.ToString());
            }
        }

        if (nsParts.Count == 0)
        {
            return null;
        }

        nsParts.Reverse();
        return string.Join(".", nsParts);
    }

    private static bool TryGetGenericTypeArgumentName(TypeSyntax typeSyntax, string expectedGenericName, out string entityName)
    {
        entityName = string.Empty;
        var generic = typeSyntax switch
        {
            GenericNameSyntax g => g,
            QualifiedNameSyntax { Right: GenericNameSyntax g } => g,
            _ => null
        };

        if (generic is null ||
            !string.Equals(generic.Identifier.ValueText, expectedGenericName, StringComparison.Ordinal) ||
            generic.TypeArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        entityName = GetSimpleTypeName(generic.TypeArgumentList.Arguments[0]);
        return !string.IsNullOrWhiteSpace(entityName);
    }

    private static string GetSimpleTypeName(TypeSyntax typeSyntax) =>
        typeSyntax switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            QualifiedNameSyntax q => q.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
            GenericNameSyntax g => g.Identifier.ValueText,
            _ => typeSyntax.ToString()
        };

    private static string StripAttributeSuffix(string name) =>
        name.Length > "Attribute".Length && name.EndsWith("Attribute", StringComparison.Ordinal)
            ? name[..^"Attribute".Length]
            : name;

    private static string DetectNewLine(string source) =>
        source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static CompilationUnitSyntax EnsureUsingDirective(
        CompilationUnitSyntax root,
        string targetNamespace,
        string newline)
    {
        var alreadyHasUsing = root.Usings.Any(u =>
            u.StaticKeyword.IsKind(SyntaxKind.None) &&
            u.Alias is null &&
            string.Equals(u.Name?.ToString(), targetNamespace, StringComparison.Ordinal));

        if (alreadyHasUsing)
        {
            return root;
        }

        var fileNamespace = root.DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault()?.Name.ToString();
        if (string.Equals(fileNamespace, targetNamespace, StringComparison.Ordinal))
        {
            return root;
        }

        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(targetNamespace))
            .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space))
            .WithTrailingTrivia(SyntaxFactory.EndOfLine(newline));

        if (root.Usings.Count > 0)
        {
            return root.WithUsings(root.Usings.Add(usingDirective));
        }

        var withBlankLine = usingDirective.WithTrailingTrivia(
            SyntaxFactory.EndOfLine(newline),
            SyntaxFactory.EndOfLine(newline));

        return root.WithUsings(SyntaxFactory.SingletonList(withBlankLine));
    }

    private sealed class EntityAnnotationSyntaxRewriter(
        ISet<string> targetEntityNames,
        ResolvedMarkerAttribute marker,
        bool readOnly,
        ISet<string>? preferredNamespaces,
        string newline) : CSharpSyntaxRewriter
    {
        public List<(string EntityName, bool AlreadyAnnotated, bool Modified)> Changes { get; } = [];
        public bool AnyModified { get; private set; }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;

            if (visited.Modifiers.Any(SyntaxKind.AbstractKeyword) ||
                visited.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                return visited;
            }

            var className = visited.Identifier.ValueText;
            if (!targetEntityNames.Contains(className))
            {
                return visited;
            }

            if (preferredNamespaces is { Count: > 0 })
            {
                var declaredNs = GetDeclaredNamespace(node);
                if (!string.IsNullOrWhiteSpace(declaredNs) && !preferredNamespaces.Contains(declaredNs))
                {
                    return visited;
                }
            }

            if (HasMarkerAttribute(visited, marker))
            {
                Changes.Add((className, AlreadyAnnotated: true, Modified: false));
                return visited;
            }

            var attrName = SyntaxFactory.ParseName(marker.AttributeShortName);
            var parsedAttr = readOnly
                ? SyntaxFactory.Attribute(attrName, SyntaxFactory.ParseAttributeArgumentList("(ReadOnly = true)"))
                : SyntaxFactory.Attribute(attrName);

            var indent = GetIndentation(visited);
            var attrList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(parsedAttr))
                .WithLeadingTrivia(visited.GetLeadingTrivia())
                .WithTrailingTrivia(SyntaxFactory.EndOfLine(newline));

            var cleanVisited = visited.WithLeadingTrivia(
                string.IsNullOrEmpty(indent)
                    ? SyntaxTriviaList.Empty
                    : SyntaxFactory.TriviaList(SyntaxFactory.Whitespace(indent)));

            AnyModified = true;
            Changes.Add((className, AlreadyAnnotated: false, Modified: true));
            return cleanVisited.WithAttributeLists(cleanVisited.AttributeLists.Insert(0, attrList));
        }

        private static bool HasMarkerAttribute(ClassDeclarationSyntax cls, ResolvedMarkerAttribute marker)
        {
            foreach (var list in cls.AttributeLists)
            {
                foreach (var attr in list.Attributes)
                {
                    var simpleName = GetSimpleTypeName(attr.Name);
                    if (string.Equals(simpleName, marker.AttributeShortName, StringComparison.Ordinal) ||
                        string.Equals(simpleName, marker.AttributeFullName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string GetIndentation(SyntaxNode node)
        {
            var leading = node.GetLeadingTrivia();
            for (var i = leading.Count - 1; i >= 0; i--)
            {
                if (leading[i].IsKind(SyntaxKind.WhitespaceTrivia))
                {
                    return leading[i].ToFullString();
                }

                if (leading[i].IsKind(SyntaxKind.EndOfLineTrivia))
                {
                    break;
                }
            }

            return string.Empty;
        }
    }
}
