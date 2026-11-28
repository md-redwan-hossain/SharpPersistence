using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpPersistence.Migrator.Cli.Translators;

internal sealed record CustomMetadataCallHint(
    string MethodName,
    bool IsColumnTarget,
    int ArgumentCount);

internal sealed record TranslatedEfCoreConfig(
    string SourceFilePath,
    string RelativePath,
    string EntityTypeName,
    string TranslatedSource,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<CustomMetadataCallHint>? CustomMetadataHints = null);

internal static class EfCoreConfigTranslator
{
    public const string DefaultOutputDirectoryName = "TranslatedEfConfigs";
    private static readonly HashSet<string> EfOnlyMethods = new(StringComparer.Ordinal)
    {
        "OwnsOne",
        "OwnsMany",
        "HasDiscriminator",
        "UsePropertyAccessMode",
        "HasData",
        "Navigation",
        "HasTrigger",
        "HasNoKey",
        "ToView",
        "ToSqlQuery",
        "ToFunction",
        "HasAlternateKey",
        "ComplexProperty"
    };

    private static readonly Dictionary<string, string> PgMethodMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["btree"] = "PostgresIndexMethod.BTree",
        ["hash"] = "PostgresIndexMethod.Hash",
        ["gist"] = "PostgresIndexMethod.Gist",
        ["spgist"] = "PostgresIndexMethod.SpGist",
        ["gin"] = "PostgresIndexMethod.Gin",
        ["brin"] = "PostgresIndexMethod.Brin"
    };

    public static TranslatedEfCoreConfig? TranslateSource(
        string sourceCode,
        string sourceFilePath = "Configuration.cs",
        string? relativePath = null,
        bool addEntityConfigurationAttribute = true,
        Func<string, string>? namespaceTransform = null)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);

        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = tree.GetCompilationUnitRoot();

        var hasConfigClass = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Any(c => TryFindEfConfigEntityName(c, out _));

        if (!hasConfigClass)
        {
            return null;
        }

        var rewriter = new EfCoreConfigSyntaxRewriter(addEntityConfigurationAttribute);
        var rewrittenRoot = (CompilationUnitSyntax)rewriter.Visit(root);
        if (namespaceTransform is not null)
        {
            rewrittenRoot = RewriteNamespaces(rewrittenRoot, namespaceTransform);
        }

        rewrittenRoot = RewriteUsings(rewrittenRoot, rewriter);

        return new TranslatedEfCoreConfig(
            sourceFilePath,
            relativePath ?? Path.GetFileName(sourceFilePath),
            rewriter.PrimaryEntityTypeName ?? "Unknown",
            rewrittenRoot.ToFullString(),
            rewriter.Warnings,
            rewriter.CustomMetadataHints);
    }

    public static string ResolveOutputDirectory(string inputDirectory, string? outputDirectory = null)
    {
        var fullInput = Path.GetFullPath(inputDirectory);
        return string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(fullInput, DefaultOutputDirectoryName)
            : Path.GetFullPath(outputDirectory);
    }

    public static IReadOnlyList<TranslatedEfCoreConfig> TranslateDirectory(
        string inputDirectory,
        string? outputDirectory = null,
        bool dryRun = false,
        bool addEntityConfigurationAttribute = true,
        string? targetNamespace = null)
    {
        var fullInput = PathGuard.EnsureReadableDirectory(inputDirectory, "--source-path");
        var rawOutput = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(fullInput, DefaultOutputDirectoryName)
            : outputDirectory;
        var fullOutput = PathGuard.EnsureWritableDirectory(rawOutput, "--output-path", dryRun);
        var fullOutputPrefix = fullOutput.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var results = new List<TranslatedEfCoreConfig>();
        foreach (var file in Directory.EnumerateFiles(fullInput, "*.cs", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (file.StartsWith(fullOutputPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var source = File.ReadAllText(file);
            var relative = Path.GetRelativePath(fullInput, file);
            var translated = TranslateSource(
                source,
                file,
                relative,
                addEntityConfigurationAttribute,
                ns => ComputeTargetNamespace(ns, fullInput, fullOutput, relative, targetNamespace));
            if (translated is null)
            {
                continue;
            }

            if (!dryRun)
            {
                var targetPath = Path.Combine(fullOutput, relative);
                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.WriteAllText(targetPath, translated.TranslatedSource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            results.Add(translated);
        }

        return results;
    }

    internal static string ComputeTargetNamespace(
        string originalNamespace,
        string fullInputDirectory,
        string fullOutputDirectory,
        string relativeFilePath,
        string? explicitRootNamespace = null)
    {
        var relDir = Path.GetDirectoryName(relativeFilePath) ?? string.Empty;
        var subFolderSegments = SplitAndSanitizePathSegments(relDir);

        if (!string.IsNullOrWhiteSpace(explicitRootNamespace))
        {
            var rootNs = explicitRootNamespace.Trim();
            return subFolderSegments.Count == 0
                ? rootNs
                : $"{rootNs}.{string.Join('.', subFolderSegments)}";
        }

        var origSegments = originalNamespace
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (origSegments.Count == 0)
        {
            return originalNamespace;
        }

        var baseSegments = origSegments.ToList();
        if (subFolderSegments.Count > 0 && baseSegments.Count > subFolderSegments.Count)
        {
            var matchesTrailingSubFolder = true;
            for (var i = 0; i < subFolderSegments.Count; i++)
            {
                if (!string.Equals(
                        baseSegments[baseSegments.Count - subFolderSegments.Count + i],
                        subFolderSegments[i],
                        StringComparison.OrdinalIgnoreCase))
                {
                    matchesTrailingSubFolder = false;
                    break;
                }
            }

            if (matchesTrailingSubFolder)
            {
                baseSegments.RemoveRange(baseSegments.Count - subFolderSegments.Count, subFolderSegments.Count);
            }
        }

        var relInputToOutput = Path.GetRelativePath(fullInputDirectory, fullOutputDirectory);
        if (Path.IsPathRooted(relInputToOutput))
        {
            var fallback = SanitizeNamespaceSegment(
                Path.GetFileName(fullOutputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            if (string.IsNullOrEmpty(fallback))
            {
                fallback = DefaultOutputDirectoryName;
            }

            baseSegments.Add(fallback);
            baseSegments.AddRange(subFolderSegments);
            return string.Join('.', baseSegments);
        }

        var rawParts = relInputToOutput.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        var upCount = 0;
        var downSegments = new List<string>();
        foreach (var part in rawParts)
        {
            if (part == ".")
            {
                continue;
            }

            if (part == ".." && downSegments.Count == 0)
            {
                upCount++;
            }
            else
            {
                var sanitized = SanitizeNamespaceSegment(part);
                if (!string.IsNullOrEmpty(sanitized))
                {
                    downSegments.Add(sanitized);
                }
            }
        }

        if (upCount == 0 && downSegments.Count == 0)
        {
            return originalNamespace;
        }

        if (upCount > 0 && upCount < baseSegments.Count)
        {
            baseSegments.RemoveRange(baseSegments.Count - upCount, upCount);
        }
        else if (upCount >= baseSegments.Count)
        {
            var fallback = downSegments.Count > 0 ? downSegments[^1] : DefaultOutputDirectoryName;
            baseSegments.Add(fallback);
            baseSegments.AddRange(subFolderSegments);
            return string.Join('.', baseSegments);
        }

        baseSegments.AddRange(downSegments);
        baseSegments.AddRange(subFolderSegments);
        return string.Join('.', baseSegments);
    }

    private static List<string> SplitAndSanitizePathSegments(string relativeDir)
    {
        var segments = new List<string>();
        if (string.IsNullOrWhiteSpace(relativeDir))
        {
            return segments;
        }

        foreach (var part in relativeDir.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (part is "." or "..")
            {
                continue;
            }

            var sanitized = SanitizeNamespaceSegment(part);
            if (!string.IsNullOrEmpty(sanitized))
            {
                segments.Add(sanitized);
            }
        }

        return segments;
    }

    private static string SanitizeNamespaceSegment(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw.Trim())
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }

        if (sb.Length > 0 && char.IsDigit(sb[0]))
        {
            sb.Insert(0, '_');
        }

        return sb.ToString();
    }

    private static CompilationUnitSyntax RewriteNamespaces(
        CompilationUnitSyntax root,
        Func<string, string> namespaceTransform)
    {
        var rewriter = new NamespaceRewriter(namespaceTransform);
        return (CompilationUnitSyntax)rewriter.Visit(root);
    }

    private sealed class NamespaceRewriter(Func<string, string> transform) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            var visited = (FileScopedNamespaceDeclarationSyntax)base.VisitFileScopedNamespaceDeclaration(node)!;
            var oldName = visited.Name.ToString();
            var newName = transform(oldName);
            return string.Equals(oldName, newName, StringComparison.Ordinal)
                ? visited
                : visited.WithName(SyntaxFactory.ParseName(newName).WithTriviaFrom(visited.Name));
        }

        public override SyntaxNode? VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            var visited = (NamespaceDeclarationSyntax)base.VisitNamespaceDeclaration(node)!;
            var oldName = visited.Name.ToString();
            var newName = transform(oldName);
            return string.Equals(oldName, newName, StringComparison.Ordinal)
                ? visited
                : visited.WithName(SyntaxFactory.ParseName(newName).WithTriviaFrom(visited.Name));
        }
    }

    private static bool TryFindEfConfigEntityName(ClassDeclarationSyntax classDecl, out string entityTypeName)
    {
        if (classDecl.BaseList is not null)
        {
            foreach (var baseType in classDecl.BaseList.Types)
            {
                if (TryExtractGenericArg(baseType.Type, "IEntityTypeConfiguration", out entityTypeName))
                {
                    return true;
                }
            }
        }

        entityTypeName = string.Empty;
        return false;
    }

    private static bool TryExtractGenericArg(TypeSyntax typeSyntax, string expectedSimpleName, out string typeArg)
    {
        var generic = typeSyntax switch
        {
            GenericNameSyntax g => g,
            QualifiedNameSyntax { Right: GenericNameSyntax g } => g,
            _ => null
        };

        if (generic is not null &&
            string.Equals(generic.Identifier.ValueText, expectedSimpleName, StringComparison.Ordinal) &&
            generic.TypeArgumentList.Arguments.Count == 1)
        {
            typeArg = generic.TypeArgumentList.Arguments[0].ToString();
            return true;
        }

        typeArg = string.Empty;
        return false;
    }

    private static CompilationUnitSyntax RewriteUsings(
        CompilationUnitSyntax root,
        EfCoreConfigSyntaxRewriter state)
    {
        var existingNames = new HashSet<string>(StringComparer.Ordinal);
        var keptUsings = new List<UsingDirectiveSyntax>();

        foreach (var u in root.Usings)
        {
            var name = u.Name?.ToString() ?? string.Empty;
            if (string.Equals(name, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                string.Equals(name, "Microsoft.EntityFrameworkCore.Metadata.Builders", StringComparison.Ordinal))
            {
                continue;
            }

            keptUsings.Add(u);
            existingNames.Add(name);
        }

        var requiredUsings = new List<string>
        {
            "SharpPersistence.Migrator.Abstractions",
            "SharpPersistence.Migrator.Builders"
        };

        if (state.UsesAttributes)
        {
            requiredUsings.Add("SharpPersistence.Migrator.Attributes");
        }

        if (state.UsesEnums)
        {
            requiredUsings.Add("SharpPersistence.Migrator.Enums");
        }

        if (state.UsesEfCoreExtensions)
        {
            requiredUsings.Add("SharpPersistence.Migrator.EfCore");
        }

        if (state.UsesPostgresExtensions)
        {
            requiredUsings.Add("SharpPersistence.Migrator.PostgreSql");
        }

        foreach (var req in requiredUsings)
        {
            if (existingNames.Add(req))
            {
                keptUsings.Add(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(req))
                    .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space))
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed)));
            }
        }

        return root.WithUsings(SyntaxFactory.List(keptUsings));
    }

    private sealed class EfCoreConfigSyntaxRewriter(bool addEntityConfigurationAttribute) : CSharpSyntaxRewriter
    {
        private string? _activeBuilderParam;

        public string? PrimaryEntityTypeName { get; private set; }
        public bool UsesAttributes { get; private set; }
        public bool UsesEnums { get; private set; }
        public bool UsesEfCoreExtensions { get; private set; }
        public bool UsesPostgresExtensions { get; private set; }
        public List<string> Warnings { get; } = [];

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (!TryFindEfConfigEntityName(node, out var entityTypeName))
            {
                return base.VisitClassDeclaration(node);
            }

            PrimaryEntityTypeName ??= entityTypeName;

            var updated = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;

            // Replace IEntityTypeConfiguration<T> -> IEntityConfiguration<T>
            if (updated.BaseList is not null)
            {
                var newTypes = new List<BaseTypeSyntax>(updated.BaseList.Types.Count);
                foreach (var bt in updated.BaseList.Types)
                {
                    if (TryExtractGenericArg(bt.Type, "IEntityTypeConfiguration", out var arg))
                    {
                        var replacementType = SyntaxFactory.ParseTypeName($"IEntityConfiguration<{arg}>")
                            .WithTriviaFrom(bt.Type);
                        newTypes.Add(SyntaxFactory.SimpleBaseType(replacementType).WithTriviaFrom(bt));
                    }
                    else
                    {
                        newTypes.Add(bt);
                    }
                }

                updated = updated.WithBaseList(updated.BaseList.WithTypes(SyntaxFactory.SeparatedList(newTypes)));
            }

            // Ensure sealed modifier (if not already sealed or abstract)
            if (!updated.Modifiers.Any(m => m.IsKind(SyntaxKind.SealedKeyword) || m.IsKind(SyntaxKind.AbstractKeyword)))
            {
                var sealedToken = SyntaxFactory.Token(SyntaxKind.SealedKeyword).WithTrailingTrivia(SyntaxFactory.Space);
                var modifiers = updated.Modifiers;
                var insertIdx = modifiers.Count;
                for (var i = 0; i < modifiers.Count; i++)
                {
                    if (modifiers[i].IsKind(SyntaxKind.PartialKeyword))
                    {
                        insertIdx = i;
                        break;
                    }
                }

                updated = updated.WithModifiers(modifiers.Insert(insertIdx, sealedToken));
            }

            // Add [EntityConfiguration<TEntity>] attribute if requested
            if (addEntityConfigurationAttribute &&
                !updated.AttributeLists.SelectMany(l => l.Attributes)
                    .Any(a => a.Name.ToString().Contains("EntityConfiguration", StringComparison.Ordinal)))
            {
                UsesAttributes = true;
                var originalLeadingTrivia = updated.GetLeadingTrivia();
                var indentTrivia = GetIndentationTrivia(updated);

                if (node.Parent is FileScopedNamespaceDeclarationSyntax &&
                    !originalLeadingTrivia.Any(t => t.IsKind(SyntaxKind.EndOfLineTrivia)))
                {
                    originalLeadingTrivia = originalLeadingTrivia.Insert(0, SyntaxFactory.CarriageReturnLineFeed);
                }

                var attrList = SyntaxFactory.AttributeList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Attribute(SyntaxFactory.ParseName($"EntityConfiguration<{entityTypeName}>"))))
                    .WithLeadingTrivia(originalLeadingTrivia)
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

                updated = updated
                    .WithLeadingTrivia(indentTrivia)
                    .WithAttributeLists(updated.AttributeLists.Insert(0, attrList));
            }

            return updated;
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (string.Equals(node.Identifier.ValueText, "Configure", StringComparison.Ordinal) &&
                node.ParameterList.Parameters.Count == 1 &&
                node.ParameterList.Parameters[0].Type is TypeSyntax paramType &&
                TryExtractGenericArg(paramType, "EntityTypeBuilder", out var entityArg))
            {
                var param = node.ParameterList.Parameters[0];
                _activeBuilderParam = param.Identifier.ValueText;

                var newParamType = SyntaxFactory.ParseTypeName($"EntityBuilder<{entityArg}>")
                    .WithTriviaFrom(paramType);
                var newParam = param.WithType(newParamType);
                var newParamList = node.ParameterList.WithParameters(SyntaxFactory.SingletonSeparatedList(newParam));

                var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node.WithParameterList(newParamList))!;
                _activeBuilderParam = null;
                return visited;
            }

            return base.VisitMethodDeclaration(node);
        }

        public override SyntaxNode? VisitBlock(BlockSyntax node)
        {
            if (_activeBuilderParam is null)
            {
                return base.VisitBlock(node);
            }

            var newStatements = new List<StatementSyntax>();
            foreach (var stmt in node.Statements)
            {
                if (stmt is ExpressionStatementSyntax exprStmt)
                {
                    if (TryFlattenToTableCallback(exprStmt, _activeBuilderParam, out var flattened))
                    {
                        newStatements.AddRange(flattened);
                        continue;
                    }

                    if (TryRewriteHasOneChain(exprStmt, _activeBuilderParam, out var rewrittenFk))
                    {
                        newStatements.Add(rewrittenFk);
                        continue;
                    }

                    var visitedStmt = (ExpressionStatementSyntax)Visit(exprStmt);
                    visitedStmt = FlagEfOnlyCalls(visitedStmt);
                    newStatements.Add(visitedStmt);
                }
                else
                {
                    newStatements.Add((StatementSyntax)Visit(stmt));
                }
            }

            return node.WithStatements(SyntaxFactory.List(newStatements));
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;

            if (visited.Expression is MemberAccessExpressionSyntax member)
            {
                var methodName = member.Name.Identifier.ValueText;

                if (string.Equals(methodName, "HasConversion", StringComparison.Ordinal) &&
                    visited.ArgumentList.Arguments.Count <= 1)
                {
                    UsesEfCoreExtensions = true;
                }

                // Rewrite <target>.Metadata.SetAnnotation(...) -> <target>.SetAnnotation(...)
                // and <target>.HasAnnotation(...) -> <target>.SetAnnotation(...)
                if (string.Equals(methodName, "SetAnnotation", StringComparison.Ordinal) &&
                    member.Expression is MemberAccessExpressionSyntax annMetaAccess &&
                    string.Equals(annMetaAccess.Name.Identifier.ValueText, "Metadata", StringComparison.Ordinal))
                {
                    var newMember = member.WithExpression(annMetaAccess.Expression);
                    return visited.WithExpression(newMember);
                }

                if (string.Equals(methodName, "HasAnnotation", StringComparison.Ordinal))
                {
                    var newMember = member.WithName(SyntaxFactory.IdentifierName("SetAnnotation"));
                    return visited.WithExpression(newMember);
                }

                // Rewrite builder.Metadata.HasSchema("sales") / SetSchema("sales") -> builder.HasSchema("sales")
                if (_activeBuilderParam is not null &&
                    (string.Equals(methodName, "HasSchema", StringComparison.Ordinal) ||
                     string.Equals(methodName, "SetSchema", StringComparison.Ordinal)) &&
                    member.Expression is MemberAccessExpressionSyntax metaAccess &&
                    string.Equals(metaAccess.Name.Identifier.ValueText, "Metadata", StringComparison.Ordinal) &&
                    string.Equals(metaAccess.Expression.ToString(), _activeBuilderParam, StringComparison.Ordinal))
                {
                    var newMember = member.WithExpression(SyntaxFactory.IdentifierName(_activeBuilderParam))
                        .WithName(SyntaxFactory.IdentifierName("HasSchema"));
                    return visited.WithExpression(newMember);
                }

                // Strip .Metadata on any custom EF Core extension call (e.g. .Property(x => x.Name).Metadata.SkipAutomatedIndexCreation())
                if (member.Expression is MemberAccessExpressionSyntax customMetaAccess &&
                    string.Equals(customMetaAccess.Name.Identifier.ValueText, "Metadata", StringComparison.Ordinal))
                {
                    var isColumnTarget = IsPropertyBuilderExpression(customMetaAccess.Expression);
                    CustomMetadataHints.Add(new CustomMetadataCallHint(
                        methodName,
                        isColumnTarget,
                        visited.ArgumentList.Arguments.Count));
                    var newMember = member.WithExpression(customMetaAccess.Expression);
                    return visited.WithExpression(newMember);
                }

                // Rewrite builder.ToTable("orders", "sales") -> builder.ToTable("orders").HasSchema("sales")
                if (_activeBuilderParam is not null &&
                    string.Equals(methodName, "ToTable", StringComparison.Ordinal) &&
                    string.Equals(member.Expression.ToString(), _activeBuilderParam, StringComparison.Ordinal) &&
                    visited.ArgumentList.Arguments.Count == 2 &&
                    visited.ArgumentList.Arguments[1].Expression is not LambdaExpressionSyntax)
                {
                    var tableArg = visited.ArgumentList.Arguments[0];
                    var schemaArg = visited.ArgumentList.Arguments[1];
                    return SyntaxFactory.ParseExpression($"{_activeBuilderParam}.ToTable({tableArg}).HasSchema({schemaArg})")
                        .WithTriviaFrom(visited);
                }

                // Rewrite HasMethod("gin") -> HasMethod(PostgresIndexMethod.Gin)
                if (string.Equals(methodName, "HasMethod", StringComparison.Ordinal) &&
                    visited.ArgumentList.Arguments.Count == 1 &&
                    visited.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit &&
                    lit.IsKind(SyntaxKind.StringLiteralExpression) &&
                    PgMethodMap.TryGetValue(lit.Token.ValueText, out var pgEnumExpr))
                {
                    UsesPostgresExtensions = true;
                    var arg = SyntaxFactory.Argument(SyntaxFactory.ParseExpression(pgEnumExpr));
                    return visited.WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(arg)));
                }

                // Rewrite Npgsql HasOperators(...) -> HasOperator(...)
                if (string.Equals(methodName, "HasOperators", StringComparison.Ordinal) &&
                    TryRewriteHasOperators(visited, member, out var rewrittenOperators))
                {
                    UsesPostgresExtensions = true;
                    return rewrittenOperators;
                }
            }

            return visited;
        }

        public List<CustomMetadataCallHint> CustomMetadataHints { get; } = [];

        private static bool IsPropertyBuilderExpression(ExpressionSyntax expr)
        {
            ExpressionSyntax? current = expr;
            while (current is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax ma)
            {
                if (string.Equals(ma.Name.Identifier.ValueText, "Property", StringComparison.Ordinal))
                {
                    return true;
                }

                current = ma.Expression;
            }

            return false;
        }

        private bool TryRewriteHasOperators(
            InvocationExpressionSyntax visited,
            MemberAccessExpressionSyntax member,
            out ExpressionSyntax rewritten)
        {
            rewritten = visited;
            var opExprs = ExtractOperatorExpressions(visited.ArgumentList);
            if (opExprs is null || !TryExtractIndexPropertyNames(member.Expression, out var indexProps) || indexProps.Count == 0)
            {
                const string warning = "HasOperators(...) could not be mapped automatically; update to HasOperator(...) manually.";
                Warnings.Add(warning);
                return false;
            }

            var nonSkipped = new List<(int Index, ExpressionSyntax Expr)>();
            for (var i = 0; i < opExprs.Count && i < indexProps.Count; i++)
            {
                if (!IsSkippedOperatorExpression(opExprs[i]))
                {
                    nonSkipped.Add((i, opExprs[i]));
                }
            }

            if (nonSkipped.Count == 0)
            {
                rewritten = member.Expression.WithTrailingTrivia(visited.GetTrailingTrivia());
                return true;
            }

            if (indexProps.Count == 1)
            {
                var arg = SyntaxFactory.Argument(nonSkipped[0].Expr.WithoutTrivia());
                var newMember = member.WithName(SyntaxFactory.IdentifierName("HasOperator").WithTriviaFrom(member.Name));
                rewritten = visited
                    .WithExpression(newMember)
                    .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(arg)));
                return true;
            }

            ExpressionSyntax current = member.Expression;
            var firstDotTrivia = member.OperatorToken.LeadingTrivia;
            for (var n = 0; n < nonSkipped.Count; n++)
            {
                var (propIdx, opExpr) = nonSkipped[n];
                var propName = indexProps[propIdx];
                var keyArg = SyntaxFactory.Argument(SyntaxFactory.ParseExpression($"k => k.{propName}"));
                var opArg = SyntaxFactory.Argument(opExpr.WithoutTrivia());
                var args = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
                [
                    keyArg,
                    opArg
                ],
                [
                    SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space)
                ]));

                var dot = SyntaxFactory.Token(SyntaxKind.DotToken).WithLeadingTrivia(firstDotTrivia);
                var access = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    current,
                    dot,
                    SyntaxFactory.IdentifierName("HasOperator"));
                current = SyntaxFactory.InvocationExpression(access, args);
            }

            rewritten = current.WithTrailingTrivia(visited.GetTrailingTrivia());
            return true;
        }

        private static List<ExpressionSyntax>? ExtractOperatorExpressions(ArgumentListSyntax argumentList)
        {
            var args = argumentList.Arguments;
            if (args.Count == 0)
            {
                return [];
            }

            if (args.Count == 1)
            {
                switch (args[0].Expression)
                {
                    case CollectionExpressionSyntax coll:
                        return coll.Elements.OfType<ExpressionElementSyntax>().Select(e => e.Expression).ToList();
                    case ImplicitArrayCreationExpressionSyntax impArr:
                        return [.. impArr.Initializer.Expressions];
                    case ArrayCreationExpressionSyntax arr when arr.Initializer is not null:
                        return [.. arr.Initializer.Expressions];
                }
            }

            return args.Select(a => a.Expression).ToList();
        }

        private static bool TryExtractIndexPropertyNames(ExpressionSyntax expr, out List<string> propertyNames)
        {
            propertyNames = [];
            ExpressionSyntax? current = expr;
            while (current is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax ma)
            {
                if (string.Equals(ma.Name.Identifier.ValueText, "HasIndex", StringComparison.Ordinal))
                {
                    if (inv.ArgumentList.Arguments.Count == 1 &&
                        inv.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax lambda)
                    {
                        var body = lambda.ExpressionBody;
                        if (body is MemberAccessExpressionSyntax singleMa)
                        {
                            propertyNames.Add(singleMa.Name.Identifier.ValueText);
                            return true;
                        }

                        if (body is AnonymousObjectCreationExpressionSyntax anon)
                        {
                            foreach (var init in anon.Initializers)
                            {
                                if (init.Expression is MemberAccessExpressionSyntax propMa)
                                {
                                    propertyNames.Add(propMa.Name.Identifier.ValueText);
                                }
                                else if (init.NameEquals is not null)
                                {
                                    propertyNames.Add(init.NameEquals.Name.Identifier.ValueText);
                                }
                            }

                            return propertyNames.Count > 0;
                        }
                    }

                    return false;
                }

                current = ma.Expression;
            }

            return false;
        }

        private static bool IsSkippedOperatorExpression(ExpressionSyntax expr)
        {
            if (expr.IsKind(SyntaxKind.NullLiteralExpression))
            {
                return true;
            }

            if (expr is LiteralExpressionSyntax lit &&
                lit.IsKind(SyntaxKind.StringLiteralExpression) &&
                string.IsNullOrWhiteSpace(lit.Token.ValueText))
            {
                return true;
            }

            if (expr is MemberAccessExpressionSyntax ma &&
                string.Equals(ma.Name.Identifier.ValueText, "Empty", StringComparison.Ordinal))
            {
                var target = ma.Expression.ToString();
                if (string.Equals(target, "string", StringComparison.Ordinal) ||
                    string.Equals(target, "String", StringComparison.Ordinal) ||
                    string.Equals(target, "System.String", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
            if (string.Equals(visited.Expression.ToString(), "DeleteBehavior", StringComparison.Ordinal))
            {
                UsesEnums = true;
                var mapped = visited.Name.Identifier.ValueText switch
                {
                    "Cascade" or "ClientCascade" => "Cascade",
                    "SetNull" => "SetNull",
                    "Restrict" => "Restrict",
                    _ => "NoAction"
                };

                return SyntaxFactory.ParseExpression($"DbReferentialAction.{mapped}").WithTriviaFrom(visited);
            }

            return visited;
        }

        private bool TryFlattenToTableCallback(
            ExpressionStatementSyntax stmt,
            string builderParam,
            out List<StatementSyntax> output)
        {
            output = [];
            if (stmt.Expression is not InvocationExpressionSyntax inv ||
                inv.Expression is not MemberAccessExpressionSyntax ma ||
                !string.Equals(ma.Expression.ToString(), builderParam, StringComparison.Ordinal) ||
                !string.Equals(ma.Name.Identifier.ValueText, "ToTable", StringComparison.Ordinal))
            {
                return false;
            }

            var args = inv.ArgumentList.Arguments;
            if (args.Count == 0 || args[^1].Expression is not SimpleLambdaExpressionSyntax lambda)
            {
                return false;
            }

            var indent = GetIndentationString(stmt);
            var nonLambdaArgs = args.Take(args.Count - 1).ToList();
            if (nonLambdaArgs.Count == 1)
            {
                output.Add(SyntaxFactory.ParseStatement($"{indent}{builderParam}.ToTable({nonLambdaArgs[0]});\r\n"));
            }
            else if (nonLambdaArgs.Count == 2)
            {
                output.Add(SyntaxFactory.ParseStatement($"{indent}{builderParam}.ToTable({nonLambdaArgs[0]}).HasSchema({nonLambdaArgs[1]});\r\n"));
            }
            else if (nonLambdaArgs.Count > 2)
            {
                var argsText = string.Join(", ", nonLambdaArgs.Select(a => a.ToString()));
                output.Add(SyntaxFactory.ParseStatement($"{indent}{builderParam}.ToTable({argsText});\r\n"));
            }

            var tableParam = lambda.Parameter.Identifier.ValueText;
            var innerStatements = lambda.Block is not null
                ? lambda.Block.Statements.OfType<ExpressionStatementSyntax>().ToList()
                : (lambda.ExpressionBody is ExpressionSyntax bodyExpr
                    ? [SyntaxFactory.ExpressionStatement(bodyExpr)]
                    : []);

            foreach (var inner in innerStatements)
            {
                var visitedInner = (ExpressionStatementSyntax)Visit(inner);
                var innerText = ReplaceLeadingIdentifier(visitedInner.Expression.ToString(), tableParam, builderParam);
                var parsed = (ExpressionStatementSyntax)SyntaxFactory.ParseStatement($"{indent}{innerText};\r\n");
                output.Add(FlagEfOnlyCalls(parsed));
            }

            return true;
        }

        private bool TryRewriteHasOneChain(
            ExpressionStatementSyntax stmt,
            string builderParam,
            out StatementSyntax rewritten)
        {
            rewritten = stmt;
            if (stmt.Expression is not InvocationExpressionSyntax rootInv)
            {
                return false;
            }

            // Unwind fluent chain from root back to builderParam
            var calls = new List<(string Name, TypeArgumentListSyntax? TypeArgs, ArgumentListSyntax Args)>();
            ExpressionSyntax current = rootInv;
            while (current is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax ma)
            {
                var typeArgs = ma.Name is GenericNameSyntax gn ? gn.TypeArgumentList : null;
                calls.Add((ma.Name.Identifier.ValueText, typeArgs, inv.ArgumentList));
                current = ma.Expression;
            }

            if (!string.Equals(current.ToString(), builderParam, StringComparison.Ordinal))
            {
                return false;
            }

            calls.Reverse();
            if (calls.Count == 0 || !string.Equals(calls[0].Name, "HasOne", StringComparison.Ordinal))
            {
                return false;
            }

            string? principalType = calls[0].TypeArgs?.Arguments.FirstOrDefault()?.ToString();
            string? fkExpr = null;
            string? principalKeyExpr = null;
            string? constraintNameExpr = null;
            string? onDeleteExpr = null;
            var isOneToOne = false;

            foreach (var (name, _, args) in calls.Skip(1))
            {
                switch (name)
                {
                    case "WithOne":
                        isOneToOne = true;
                        break;
                    case "HasForeignKey":
                        fkExpr = args.Arguments.FirstOrDefault()?.ToString();
                        break;
                    case "HasPrincipalKey":
                        principalKeyExpr = args.Arguments.FirstOrDefault()?.ToString();
                        break;
                    case "HasConstraintName":
                        constraintNameExpr = args.Arguments.FirstOrDefault()?.ToString();
                        break;
                    case "OnDelete":
                        if (args.Arguments.Count == 1)
                        {
                            var visitedArg = Visit(args.Arguments[0].Expression);
                            onDeleteExpr = visitedArg?.ToString();
                        }
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(fkExpr))
            {
                return false;
            }

            var indent = GetIndentationString(stmt);
            var sb = new StringBuilder();
            if (string.IsNullOrWhiteSpace(principalType))
            {
                const string warning = "HasOne(...) did not specify an explicit generic type; update References<TPrincipal>(...) manually.";
                Warnings.Add(warning);
                sb.AppendLine($"// TODO(SharpPersistence.Migrator): {warning}");
                sb.Append(indent);
                principalType = "/* TPrincipal */ object";
            }

            var refExpr = string.IsNullOrWhiteSpace(principalKeyExpr) ? "p => p.Id" : principalKeyExpr;
            sb.AppendLine($"{builderParam}.HasForeignKey({fkExpr})");
            sb.Append($"{indent}    .References<{principalType}>({refExpr})");

            if (isOneToOne)
            {
                sb.AppendLine();
                sb.Append($"{indent}    .IsOneToOne()");
            }

            if (!string.IsNullOrWhiteSpace(constraintNameExpr))
            {
                sb.AppendLine();
                sb.Append($"{indent}    .HasConstraintName({constraintNameExpr})");
            }

            if (!string.IsNullOrWhiteSpace(onDeleteExpr))
            {
                sb.AppendLine();
                sb.Append($"{indent}    .OnDelete({onDeleteExpr})");
            }

            sb.AppendLine(";");
            rewritten = SyntaxFactory.ParseStatement(sb.ToString())
                .WithLeadingTrivia(stmt.GetLeadingTrivia());
            return true;
        }

        private ExpressionStatementSyntax FlagEfOnlyCalls(ExpressionStatementSyntax stmt)
        {
            var matched = stmt.DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Select(m => m.Name.Identifier.ValueText)
                .FirstOrDefault(EfOnlyMethods.Contains);

            if (matched is null)
            {
                return stmt;
            }

            var warning = $"EF Core-specific call '{matched}' requires manual review.";
            Warnings.Add(warning);
            var indent = GetIndentationTrivia(stmt);
            var comment = SyntaxFactory.Comment($"// TODO(SharpPersistence.Migrator): {warning}");
            var newLeading = stmt.GetLeadingTrivia()
                .Insert(0, SyntaxFactory.CarriageReturnLineFeed)
                .Insert(0, comment)
                .Insert(0, indent);

            return stmt.WithLeadingTrivia(newLeading);
        }

        private static string ReplaceLeadingIdentifier(string expr, string oldIdent, string newIdent)
        {
            if (expr.StartsWith(oldIdent + ".", StringComparison.Ordinal))
            {
                return newIdent + expr[oldIdent.Length..];
            }

            return expr;
        }

        private static SyntaxTrivia GetIndentationTrivia(SyntaxNode node)
        {
            var ws = node.GetLeadingTrivia()
                .Reverse()
                .FirstOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
            return ws == default ? SyntaxFactory.Whitespace(string.Empty) : ws;
        }

        private static string GetIndentationString(SyntaxNode node) =>
            GetIndentationTrivia(node).ToString();
    }
}
