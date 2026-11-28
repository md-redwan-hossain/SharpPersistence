using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpPersistence.Migrator.Cli.Translators;

internal sealed record ResolvedMarkerAttribute(
    string AttributeShortName,
    string AttributeFullName,
    string? AttributeNamespace,
    string? GroupNamespace = null);

internal sealed record AnnotatedEntityItem(
    string EntityName,
    string FilePath,
    string RelativePath,
    bool AlreadyAnnotated,
    bool Modified);

internal sealed record EntityAnnotationResult(
    ResolvedMarkerAttribute MarkerAttribute,
    IReadOnlyList<AnnotatedEntityItem> Items,
    IReadOnlyList<string> Warnings);

internal static class EntityAnnotator
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
                DefaultAttributeNamespace);
        }

        var trimmedName = entityGroupName!.Trim();
        var trimmedNs = entityGroupNamespace!.Trim();

        return new ResolvedMarkerAttribute(
            $"{DefaultAttributeName}<{trimmedName}>",
            $"EntityAttribute<{trimmedName}>",
            DefaultAttributeNamespace,
            trimmedNs);
    }

    public static EntityAnnotationResult AnnotateDirectory(
        string directory,
        string? entityGroupName = null,
        string? entityGroupNamespace = null,
        bool readOnly = false,
        bool dryRun = false)
    {
        var fullDir = PathGuard.EnsureReadableDirectory(directory, "--directory");
        var marker = ResolveMarkerAttribute(entityGroupName, entityGroupNamespace);
        var items = new List<AnnotatedEntityItem>();
        var warnings = new List<string>();

        foreach (var file in EnumerateCsFiles(fullDir))
        {
            var source = File.ReadAllText(file);
            var (updated, changes, modified) = AnnotateSource(source, marker, readOnly);
            if (changes.Count == 0)
            {
                continue;
            }

            if (modified && !dryRun)
            {
                File.WriteAllText(file, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            var relPath = Path.GetRelativePath(fullDir, file).Replace('\\', '/');
            foreach (var (entityName, alreadyAnnotated, itemModified) in changes)
            {
                items.Add(new AnnotatedEntityItem(entityName, file, relPath, alreadyAnnotated, itemModified));
            }
        }

        if (items.Count == 0)
        {
            warnings.Add($"No eligible entity classes found under '{fullDir}'.");
        }

        return new EntityAnnotationResult(marker, items, warnings);
    }

    public static (string UpdatedSource, IReadOnlyList<(string EntityName, bool AlreadyAnnotated, bool Modified)> Changes, bool Modified)
        AnnotateSource(string sourceCode, ResolvedMarkerAttribute marker, bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(marker);

        var newline = DetectNewLine(sourceCode);
        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = tree.GetCompilationUnitRoot();

        var rewriter = new EntityAnnotationSyntaxRewriter(marker, readOnly, newline);
        var rewrittenRoot = (CompilationUnitSyntax)rewriter.Visit(root)!;

        if (!rewriter.AnyModified)
        {
            return (sourceCode, rewriter.Changes, false);
        }

        if (!string.IsNullOrWhiteSpace(marker.AttributeNamespace))
        {
            rewrittenRoot = EnsureUsingDirective(rewrittenRoot, marker.AttributeNamespace, newline);
        }

        if (!string.IsNullOrWhiteSpace(marker.GroupNamespace))
        {
            rewrittenRoot = EnsureUsingDirective(rewrittenRoot, marker.GroupNamespace, newline);
        }

        return (rewrittenRoot.ToFullString(), rewriter.Changes, true);
    }

    private static IEnumerable<string> EnumerateCsFiles(string rootDir)
    {
        var stack = new Stack<string>();
        stack.Push(rootDir);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var file in Directory.GetFiles(current, "*.cs").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }

            foreach (var sub in Directory.GetDirectories(current).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(sub);
                if (!SkippedDirectories.Contains(name))
                {
                    stack.Push(sub);
                }
            }
        }
    }

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
        ResolvedMarkerAttribute marker,
        bool readOnly,
        string newline) : CSharpSyntaxRewriter
    {
        public List<(string EntityName, bool AlreadyAnnotated, bool Modified)> Changes { get; } = [];
        public bool AnyModified { get; private set; }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            // Skip nested classes so helper types inside an entity are not marked as entities
            if (node.Parent is TypeDeclarationSyntax)
            {
                return node;
            }

            if (node.Modifiers.Any(SyntaxKind.AbstractKeyword) ||
                node.Modifiers.Any(SyntaxKind.StaticKeyword) ||
                node.TypeParameterList is not null)
            {
                return node;
            }

            var className = node.Identifier.ValueText;
            if (HasMarkerAttribute(node, marker))
            {
                Changes.Add((className, AlreadyAnnotated: true, Modified: false));
                return node;
            }

            var attrName = SyntaxFactory.ParseName(marker.AttributeShortName);
            var parsedAttr = readOnly
                ? SyntaxFactory.Attribute(attrName, SyntaxFactory.ParseAttributeArgumentList("(ReadOnly = true)"))
                : SyntaxFactory.Attribute(attrName);

            var indent = GetIndentation(node);
            var attrList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(parsedAttr))
                .WithLeadingTrivia(node.GetLeadingTrivia())
                .WithTrailingTrivia(SyntaxFactory.EndOfLine(newline));

            var cleanNode = node.WithLeadingTrivia(
                string.IsNullOrEmpty(indent)
                    ? SyntaxTriviaList.Empty
                    : SyntaxFactory.TriviaList(SyntaxFactory.Whitespace(indent)));

            AnyModified = true;
            Changes.Add((className, AlreadyAnnotated: false, Modified: true));
            return cleanNode.WithAttributeLists(cleanNode.AttributeLists.Insert(0, attrList));
        }

        private static bool HasMarkerAttribute(ClassDeclarationSyntax cls, ResolvedMarkerAttribute marker)
        {
            foreach (var list in cls.AttributeLists)
            {
                foreach (var attr in list.Attributes)
                {
                    var normalized = NormalizeAttributeName(attr.Name);
                    if (string.Equals(normalized, marker.AttributeShortName, StringComparison.Ordinal) ||
                        string.Equals(normalized, marker.AttributeFullName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string NormalizeAttributeName(NameSyntax name) =>
            name switch
            {
                QualifiedNameSyntax q => NormalizeAttributeName(q.Right),
                AliasQualifiedNameSyntax a => NormalizeAttributeName(a.Name),
                _ => name.ToString().Replace(" ", string.Empty, StringComparison.Ordinal)
            };

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
