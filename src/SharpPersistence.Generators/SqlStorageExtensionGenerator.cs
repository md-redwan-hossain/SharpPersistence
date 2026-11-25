using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SharpPersistence.Generators;

[Generator]
public sealed class SqlStorageExtensionGenerator : IIncrementalGenerator
{
    private const string DiagnosticCategory = "SharpPersistence.SqlParser";
    private const string ParsedSqlStorageMetadataName = "SharpPersistence.Abstractions.IParsedSqlStorage";
    private const string GenerateExtensionsProperty = "build_property.SharpPersistenceGenerateSqlExtensions";
    private const string RootNamespaceProperty = "build_property.RootNamespace";
    private const string ProjectDirProperty = "build_property.ProjectDir";
    private const string StartPrefix = "#start#";
    private const string EndPrefix = "#end#";

    private static readonly string[] NewLineSeparators = ["\r\n", "\n", "\r"];

    private static readonly DiagnosticDescriptor InvalidOrUnclosedStartTag = new(
        id: "SPSQL001",
        title: "Invalid or unclosed SQL start tag",
        messageFormat: "{0}",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidOrDuplicateEndTag = new(
        id: "SPSQL002",
        title: "Invalid or duplicate SQL end tag",
        messageFormat: "{0}",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor EmptySqlBlock = new(
        id: "SPSQL003",
        title: "SQL block is empty",
        messageFormat: "SQL block '{0}' is empty",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateSqlTag = new(
        id: "SPSQL004",
        title: "Duplicate SQL tag",
        messageFormat: "Duplicate SQL tag '{0}' found ({1}). Each tag must be unique.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateMethodName = new(
        id: "SPSQL005",
        title: "Generated SQL extension method name collision",
        messageFormat: "SQL tags '{0}' and '{1}' both normalize to extension method '{2}'. Rename one of the tags.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private sealed record GeneratorOptions(bool IsEnabled, string RootNamespace, string ProjectDir);

    private sealed record CompilationMeta(bool HasParsedSqlStorage, string AssemblyName);

    private sealed record SqlBlockRecord(
        string Tag,
        string SqlPreview,
        string FilePath,
        int LineIndex,
        int StartCol,
        int EndCol);

    private sealed record SqlDiagnosticRecord(
        string DiagnosticId,
        string Message,
        string FilePath,
        int LineIndex,
        int StartCol,
        int EndCol);

    private sealed record ParsedSqlFile(
        string FilePath,
        EquatableArray<SqlBlockRecord> Blocks,
        EquatableArray<SqlDiagnosticRecord> Diagnostics);

    private sealed class MutableBlockInfo
    {
        public int StartLineIndex { get; set; }
        public int StartCol { get; set; }
        public int EndCol { get; set; }
        public int EndLineIndex { get; set; }
        public bool EndFound { get; set; }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var options = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
            {
                var enabled = !(provider.GlobalOptions.TryGetValue(GenerateExtensionsProperty, out var flag) &&
                                string.Equals(flag?.Trim(), "false", StringComparison.OrdinalIgnoreCase));
                provider.GlobalOptions.TryGetValue(RootNamespaceProperty, out var rootNs);
                provider.GlobalOptions.TryGetValue(ProjectDirProperty, out var projectDir);
                return new GeneratorOptions(enabled, rootNs?.Trim() ?? string.Empty, projectDir?.Trim() ?? string.Empty);
            });

        var compilationMeta = context.CompilationProvider
            .Select(static (compilation, _) => new CompilationMeta(
                compilation.GetTypeByMetadataName(ParsedSqlStorageMetadataName) is not null,
                compilation.AssemblyName ?? "SharpPersistence"));

        var parsedSqlFiles = context.AdditionalTextsProvider
            .Where(static text => text.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(static (text, ct) => ParseSqlFile(text.Path, text.GetText(ct)?.ToString()))
            .Collect();

        var pipeline = parsedSqlFiles
            .Combine(options)
            .Combine(compilationMeta);

        context.RegisterSourceOutput(pipeline, static (spc, input) =>
        {
            var files = input.Left.Left;
            var opts = input.Left.Right;
            var meta = input.Right;

            if (!opts.IsEnabled || !meta.HasParsedSqlStorage || files.IsDefaultOrEmpty)
            {
                return;
            }

            var sortedFiles = files.OrderBy(static f => f.FilePath, StringComparer.Ordinal).ToList();

            foreach (var file in sortedFiles)
            {
                foreach (var diag in file.Diagnostics)
                {
                    ReportParsedDiagnostic(spc, diag);
                }
            }

            var seenTags = new Dictionary<string, SqlBlockRecord>(StringComparer.OrdinalIgnoreCase);
            var seenMethods = new Dictionary<string, SqlBlockRecord>(StringComparer.Ordinal);
            var validMethods = new List<(string MethodName, SqlBlockRecord Block)>();

            foreach (var file in sortedFiles)
            {
                foreach (var block in file.Blocks)
                {
                    if (seenTags.TryGetValue(block.Tag, out var existingTagBlock))
                    {
                        var existingDisplay = GetDisplayPath(existingTagBlock.FilePath, opts.ProjectDir);
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DuplicateSqlTag,
                            CreateLocation(block.FilePath, block.LineIndex, block.StartCol, block.EndCol),
                            block.Tag,
                            $"already defined in {existingDisplay}"));
                        continue;
                    }

                    seenTags[block.Tag] = block;

                    var methodName = BuildMethodName(block.Tag);
                    if (seenMethods.TryGetValue(methodName, out var existingMethodBlock))
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DuplicateMethodName,
                            CreateLocation(block.FilePath, block.LineIndex, block.StartCol, block.EndCol),
                            existingMethodBlock.Tag,
                            block.Tag,
                            methodName));
                        continue;
                    }

                    seenMethods[methodName] = block;
                    validMethods.Add((methodName, block));
                }
            }

            if (validMethods.Count == 0)
            {
                return;
            }

            validMethods.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.MethodName, b.MethodName));

            var targetNamespace = ResolveNamespace(opts.RootNamespace, meta.AssemblyName);
            var className = $"ParsedSqlStorageExtensions_{SanitizeIdentifier(meta.AssemblyName)}";
            var sb = new StringBuilder(validMethods.Count * 256);
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();

            foreach (var (_, block) in validMethods)
            {
                var escapedTagMeta = EscapeCSharpString(block.Tag);
                sb.AppendLine($"[assembly: global::System.Reflection.AssemblyMetadata(\"SharpPersistence.SqlTag\", \"{escapedTagMeta}\")]");
            }

            sb.AppendLine();
            sb.AppendLine($"namespace {targetNamespace};");
            sb.AppendLine();
            sb.AppendLine($"public static class {className}");
            sb.AppendLine("{");

            for (var i = 0; i < validMethods.Count; i++)
            {
                var (methodName, block) = validMethods[i];
                var displayPath = EscapeXml(GetDisplayPath(block.FilePath, opts.ProjectDir));
                var escapedTagXml = EscapeXml(block.Tag);
                var escapedPreviewXml = EscapeXml(block.SqlPreview);
                var escapedTagLiteral = EscapeCSharpString(block.Tag);

                if (i > 0)
                {
                    sb.AppendLine();
                }

                sb.AppendLine("    /// <summary>");
                sb.AppendLine($"    /// Returns the SQL query for tag <c>{escapedTagXml}</c> (from <c>{displayPath}</c>).");
                if (!string.IsNullOrEmpty(escapedPreviewXml))
                {
                    sb.AppendLine($"    /// <para><c>{escapedPreviewXml}</c></para>");
                }

                sb.AppendLine("    /// </summary>");
                sb.AppendLine($"    public static string {methodName}(this global::{ParsedSqlStorageMetadataName} storage)");
                sb.AppendLine($"        => storage[\"{escapedTagLiteral}\"];");
            }

            sb.AppendLine("}");

            spc.AddSource("ParsedSqlStorageExtensions.g.cs", sb.ToString());
        });
    }

    internal static IEnumerable<string> ExtractValidTags(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            yield break;
        }

        var lines = content!.Split(NewLineSeparators, StringSplitOptions.None);
        var openTags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            var lineText = lines[i];
            if (TryMatchBlockTag(lineText, StartPrefix, out var startTag, out _))
            {
                if (!string.IsNullOrEmpty(startTag) && !openTags.ContainsKey(startTag))
                {
                    openTags[startTag] = i;
                }
            }
            else if (TryMatchBlockTag(lineText, EndPrefix, out var endTag, out _))
            {
                if (!string.IsNullOrEmpty(endTag) && openTags.TryGetValue(endTag, out var startLine))
                {
                    openTags.Remove(endTag);
                    for (var j = startLine + 1; j < i; j++)
                    {
                        if (!string.IsNullOrWhiteSpace(lines[j]))
                        {
                            yield return endTag;
                            break;
                        }
                    }
                }
            }
        }
    }

    private static string SanitizeIdentifier(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        if (sb.Length == 0 || char.IsDigit(sb[0]))
        {
            sb.Insert(0, '_');
        }

        return sb.ToString();
    }

    private static ParsedSqlFile ParseSqlFile(string filePath, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ParsedSqlFile(filePath, EquatableArray<SqlBlockRecord>.Empty, EquatableArray<SqlDiagnosticRecord>.Empty);
        }

        var lines = content!.Split(NewLineSeparators, StringSplitOptions.None);
        var blocksByTag = new Dictionary<string, MutableBlockInfo>(StringComparer.OrdinalIgnoreCase);
        var tagOrder = new List<string>();
        var diagnostics = ImmutableArray.CreateBuilder<SqlDiagnosticRecord>();

        for (var i = 0; i < lines.Length; i++)
        {
            var lineText = lines[i];
            if (TryMatchBlockTag(lineText, StartPrefix, out var tag, out var col))
            {
                if (string.IsNullOrEmpty(tag))
                {
                    diagnostics.Add(new SqlDiagnosticRecord(
                        "SPSQL001",
                        "The tag name is empty in #start# declaration.",
                        filePath,
                        i,
                        col,
                        lineText.Length));
                    continue;
                }

                if (blocksByTag.ContainsKey(tag))
                {
                    diagnostics.Add(new SqlDiagnosticRecord(
                        "SPSQL004",
                        tag,
                        filePath,
                        i,
                        col,
                        lineText.Length));
                    continue;
                }

                blocksByTag[tag] = new MutableBlockInfo
                {
                    StartLineIndex = i,
                    StartCol = col,
                    EndCol = lineText.Length,
                    EndFound = false
                };
                tagOrder.Add(tag);
            }
            else if (TryMatchBlockTag(lineText, EndPrefix, out tag, out col))
            {
                if (string.IsNullOrEmpty(tag))
                {
                    diagnostics.Add(new SqlDiagnosticRecord(
                        "SPSQL002",
                        "The tag name is empty in #end# declaration.",
                        filePath,
                        i,
                        col,
                        lineText.Length));
                    continue;
                }

                if (blocksByTag.TryGetValue(tag, out var block))
                {
                    if (block.EndFound)
                    {
                        diagnostics.Add(new SqlDiagnosticRecord(
                            "SPSQL002",
                            $"Duplicate end tag '{tag}' found. Each tag must have a single end tag.",
                            filePath,
                            i,
                            col,
                            lineText.Length));
                    }
                    else
                    {
                        block.EndLineIndex = i;
                        block.EndFound = true;
                    }
                }
                else
                {
                    diagnostics.Add(new SqlDiagnosticRecord(
                        "SPSQL002",
                        $"End tag '{tag}' found without corresponding start tag.",
                        filePath,
                        i,
                        col,
                        lineText.Length));
                }
            }
        }

        var validBlocks = ImmutableArray.CreateBuilder<SqlBlockRecord>();
        foreach (var tag in tagOrder)
        {
            var info = blocksByTag[tag];
            if (!info.EndFound)
            {
                diagnostics.Add(new SqlDiagnosticRecord(
                    "SPSQL001",
                    $"End tag '{tag}' is missing.",
                    filePath,
                    info.StartLineIndex,
                    info.StartCol,
                    info.EndCol));
                continue;
            }

            var previewLines = new List<string>(3);
            for (var lineIdx = info.StartLineIndex + 1; lineIdx < info.EndLineIndex; lineIdx++)
            {
                var trimmed = lines[lineIdx].Trim();
                if (trimmed.Length > 0)
                {
                    previewLines.Add(trimmed);
                    if (previewLines.Count == 3)
                    {
                        break;
                    }
                }
            }

            if (previewLines.Count == 0)
            {
                diagnostics.Add(new SqlDiagnosticRecord(
                    "SPSQL003",
                    tag,
                    filePath,
                    info.StartLineIndex,
                    info.StartCol,
                    info.EndCol));
                continue;
            }

            var preview = BuildSqlPreview(previewLines);
            validBlocks.Add(new SqlBlockRecord(
                tag,
                preview,
                filePath,
                info.StartLineIndex,
                info.StartCol,
                info.EndCol));
        }

        return new ParsedSqlFile(
            filePath,
            new EquatableArray<SqlBlockRecord>(validBlocks.ToImmutable()),
            new EquatableArray<SqlDiagnosticRecord>(diagnostics.ToImmutable()));
    }

    private static bool TryMatchBlockTag(string lineText, string prefix, out string tag, out int col)
    {
        tag = string.Empty;
        col = 0;

        if (string.IsNullOrEmpty(lineText))
        {
            return false;
        }

        var idx = 0;
        var len = lineText.Length;
        while (idx < len && char.IsWhiteSpace(lineText[idx]))
        {
            idx++;
        }

        if (idx + 1 >= len || lineText[idx] != '-' || lineText[idx + 1] != '-')
        {
            return false;
        }

        idx += 2;
        while (idx < len && char.IsWhiteSpace(lineText[idx]))
        {
            idx++;
        }

        if (idx + prefix.Length > len ||
            string.Compare(lineText, idx, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) != 0)
        {
            return false;
        }

        col = idx + prefix.Length;
        tag = col < len ? lineText.Substring(col).Trim() : string.Empty;
        return true;
    }

    internal static string BuildMethodName(string tag)
    {
        var tokens = TokenizeTag(tag);
        if (tokens.Count == 0)
        {
            return "GetSql";
        }

        var normalized = new List<string>(tokens.Count + 2);
        for (var i = 0; i < tokens.Count; i++)
        {
            normalized.Add(NormalizeToken(tokens[i]));
        }

        if (!string.Equals(normalized[0], "Get", StringComparison.OrdinalIgnoreCase))
        {
            normalized.Insert(0, "Get");
        }

        if (!string.Equals(normalized[normalized.Count - 1], "Sql", StringComparison.OrdinalIgnoreCase))
        {
            normalized.Add("Sql");
        }

        return string.Concat(normalized);
    }

    private static List<string> TokenizeTag(string tag)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();

        for (var i = 0; i < tag.Length; i++)
        {
            var c = tag[i];
            if (!char.IsLetterOrDigit(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            if (current.Length > 0 && char.IsUpper(c))
            {
                var prev = current[current.Length - 1];
                var nextIsLower = i + 1 < tag.Length && char.IsLower(tag[i + 1]);
                if (char.IsLower(prev) || char.IsDigit(prev) || (char.IsUpper(prev) && nextIsLower))
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static string NormalizeToken(string token)
    {
        if (token.Length == 0)
        {
            return token;
        }

        var allUpper = true;
        for (var i = 0; i < token.Length; i++)
        {
            if (char.IsLetter(token[i]) && !char.IsUpper(token[i]))
            {
                allUpper = false;
                break;
            }
        }

        if (allUpper && token.Length > 1)
        {
            return char.ToUpperInvariant(token[0]) + token.Substring(1).ToLowerInvariant();
        }

        return char.ToUpperInvariant(token[0]) + token.Substring(1);
    }

    private static string BuildSqlPreview(List<string> previewLines)
    {
        var joined = string.Join(" ", previewLines);
        if (joined.Length > 180)
        {
            return joined.Substring(0, 177) + "...";
        }

        return joined;
    }

    private static void ReportParsedDiagnostic(SourceProductionContext spc, SqlDiagnosticRecord diag)
    {
        var location = CreateLocation(diag.FilePath, diag.LineIndex, diag.StartCol, diag.EndCol);
        switch (diag.DiagnosticId)
        {
            case "SPSQL001":
                spc.ReportDiagnostic(Diagnostic.Create(InvalidOrUnclosedStartTag, location, diag.Message));
                break;
            case "SPSQL002":
                spc.ReportDiagnostic(Diagnostic.Create(InvalidOrDuplicateEndTag, location, diag.Message));
                break;
            case "SPSQL003":
                spc.ReportDiagnostic(Diagnostic.Create(EmptySqlBlock, location, diag.Message));
                break;
            case "SPSQL004":
                spc.ReportDiagnostic(Diagnostic.Create(DuplicateSqlTag, location, diag.Message, "duplicate in file"));
                break;
        }
    }

    private static Location CreateLocation(string filePath, int lineIndex, int startCol, int endCol)
    {
        var safeLine = lineIndex < 0 ? 0 : lineIndex;
        var safeStart = startCol < 0 ? 0 : startCol;
        var safeEnd = endCol < safeStart ? safeStart : endCol;

        return Location.Create(
            filePath,
            default,
            new LinePositionSpan(
                new LinePosition(safeLine, safeStart),
                new LinePosition(safeLine, safeEnd)));
    }

    private static string ResolveNamespace(string rootNamespace, string assemblyName)
    {
        var candidate = !string.IsNullOrWhiteSpace(rootNamespace)
            ? rootNamespace
            : !string.IsNullOrWhiteSpace(assemblyName)
                ? assemblyName
                : "SharpPersistence.Generated";

        var segments = candidate.Split('.');
        var validSegments = new List<string>(segments.Length);
        foreach (var seg in segments)
        {
            var sb = new StringBuilder(seg.Length);
            for (var i = 0; i < seg.Length; i++)
            {
                var c = seg[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    if (sb.Length == 0 && char.IsDigit(c))
                    {
                        sb.Append('_');
                    }

                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }

            if (sb.Length > 0)
            {
                validSegments.Add(sb.ToString());
            }
        }

        return validSegments.Count > 0 ? string.Join(".", validSegments) : "SharpPersistence.Generated";
    }

    private static string GetDisplayPath(string filePath, string projectDir)
    {
        var normalizedFile = filePath.Replace('\\', '/');
        if (!string.IsNullOrWhiteSpace(projectDir))
        {
            var normalizedDir = projectDir.Replace('\\', '/');
            if (!normalizedDir.EndsWith("/", StringComparison.Ordinal))
            {
                normalizedDir += "/";
            }

            if (normalizedFile.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFile.Substring(normalizedDir.Length);
            }
        }

        return normalizedFile;
    }

    private static string EscapeXml(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");

    private static string EscapeCSharpString(string value) =>
        value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
}
