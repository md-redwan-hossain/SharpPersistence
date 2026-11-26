using System.Text.RegularExpressions;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed record RunlistScriptEntry(
    Guid Id,
    string RegistryName,
    string ScriptName,
    string FullPath,
    bool IsOverridden = false);

internal static partial class SqlScriptDependencyAnalyzer
{
    private sealed record ScriptDdlProfile(
        HashSet<string> ProvidesTables,
        HashSet<string> RequiresTables,
        HashSet<string> DropsTables,
        HashSet<string> ProvidesObjects,
        HashSet<string> RequiresObjects);

    public static List<DoctorCheckItem> ValidateOrder(IReadOnlyList<RunlistScriptEntry> orderedScripts)
    {
        var checks = new List<DoctorCheckItem>();
        if (orderedScripts.Count <= 1)
        {
            return checks;
        }

        var profiles = new Dictionary<Guid, ScriptDdlProfile>();
        var strippedByScript = new Dictionary<Guid, string>();
        var allCreatedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allCreatedObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var script in orderedScripts)
        {
            if (!File.Exists(script.FullPath))
            {
                continue;
            }

            var sql = File.ReadAllText(script.FullPath);
            var stripped = StripSqlComments(sql);
            strippedByScript[script.Id] = stripped;
            var profile = AnalyzeStripped(stripped);
            profiles[script.Id] = profile;

            foreach (var t in profile.ProvidesTables)
            {
                allCreatedTables.Add(t);
            }

            foreach (var o in profile.ProvidesObjects)
            {
                allCreatedObjects.Add(o);
            }
        }

        PopulateTypeUsage(profiles, strippedByScript, allCreatedObjects);

        var activeTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var droppedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activeObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var script in orderedScripts)
        {
            if (!profiles.TryGetValue(script.Id, out var profile))
            {
                continue;
            }

            foreach (var reqTable in profile.RequiresTables)
            {
                if (droppedTables.Contains(reqTable))
                {
                    checks.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Script '{script.RegistryName}/{script.ScriptName}' ({script.Id}) references table '{reqTable}' after it was dropped by an earlier script.",
                        Hint: "Reorder the scripts using db-migration-runlist.overrides.json."));
                }
                else if (allCreatedTables.Contains(reqTable) &&
                         !activeTables.Contains(reqTable) &&
                         !profile.ProvidesTables.Contains(reqTable))
                {
                    checks.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Script '{script.RegistryName}/{script.ScriptName}' ({script.Id}) references or alters table '{reqTable}' before it is created.",
                        Hint: "Check db-migration-runlist.overrides.json or run 'db-migrator runlist sync'."));
                }
            }

            foreach (var reqObj in profile.RequiresObjects)
            {
                if (allCreatedObjects.Contains(reqObj) &&
                    !activeObjects.Contains(reqObj) &&
                    !profile.ProvidesObjects.Contains(reqObj))
                {
                    checks.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Script '{script.RegistryName}/{script.ScriptName}' ({script.Id}) uses object '{reqObj}' before it is created.",
                        Hint: "Check db-migration-runlist.overrides.json or run 'db-migrator runlist sync'."));
                }
            }

            foreach (var t in profile.ProvidesTables)
            {
                activeTables.Add(t);
                droppedTables.Remove(t);
            }

            foreach (var o in profile.ProvidesObjects)
            {
                activeObjects.Add(o);
            }

            foreach (var d in profile.DropsTables)
            {
                activeTables.Remove(d);
                droppedTables.Add(d);
            }
        }

        return checks;
    }

    public static List<RunlistScriptEntry> SortByDependenciesAndTimestamp(
        IReadOnlyList<RunlistScriptEntry> scripts,
        IReadOnlyList<IEntityRegistry> sortedRegistries)
    {
        if (scripts.Count <= 1)
        {
            return [.. scripts];
        }

        var registryRank = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["_global"] = 0,
            ["_translated"] = 1
        };

        var rank = 2;
        foreach (var reg in sortedRegistries)
        {
            registryRank[reg.GetType().Name] = rank++;
        }

        registryRank["_deferred"] = rank + 1000;

        var orderedByTimestamp = scripts
            .OrderBy(s => ExtractTimestampPrefix(s.ScriptName), StringComparer.Ordinal)
            .ThenBy(s => registryRank.GetValueOrDefault(s.RegistryName, rank))
            .ThenBy(s => s.ScriptName, StringComparer.Ordinal)
            .ToList();

        var profiles = new Dictionary<Guid, ScriptDdlProfile>(orderedByTimestamp.Count);
        var strippedByScript = new Dictionary<Guid, string>(orderedByTimestamp.Count);
        var creatorByTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var creatorByObject = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var allCreatedObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < orderedByTimestamp.Count; i++)
        {
            var s = orderedByTimestamp[i];
            var sql = File.Exists(s.FullPath) ? File.ReadAllText(s.FullPath) : string.Empty;
            var stripped = StripSqlComments(sql);
            strippedByScript[s.Id] = stripped;
            var profile = AnalyzeStripped(stripped);
            profiles[s.Id] = profile;

            foreach (var t in profile.ProvidesTables)
            {
                creatorByTable.TryAdd(t, i);
            }

            foreach (var o in profile.ProvidesObjects)
            {
                creatorByObject.TryAdd(o, i);
                allCreatedObjects.Add(o);
            }
        }

        PopulateTypeUsage(profiles, strippedByScript, allCreatedObjects);

        var n = orderedByTimestamp.Count;
        var adj = new HashSet<int>[n];
        var inDegree = new int[n];
        var requirersByTable = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < n; i++)
        {
            adj[i] = [];
            foreach (var reqTable in profiles[orderedByTimestamp[i].Id].RequiresTables)
            {
                if (!requirersByTable.TryGetValue(reqTable, out var list))
                {
                    list = [];
                    requirersByTable[reqTable] = list;
                }

                list.Add(i);
            }
        }

        for (var i = 0; i < n; i++)
        {
            var profile = profiles[orderedByTimestamp[i].Id];
            foreach (var reqTable in profile.RequiresTables)
            {
                if (creatorByTable.TryGetValue(reqTable, out var creatorIdx) && creatorIdx != i)
                {
                    if (adj[creatorIdx].Add(i))
                    {
                        inDegree[i]++;
                    }
                }
            }

            foreach (var reqObj in profile.RequiresObjects)
            {
                if (creatorByObject.TryGetValue(reqObj, out var creatorIdx) && creatorIdx != i)
                {
                    if (adj[creatorIdx].Add(i))
                    {
                        inDegree[i]++;
                    }
                }
            }

            foreach (var dropTable in profile.DropsTables)
            {
                if (requirersByTable.TryGetValue(dropTable, out var requirers))
                {
                    foreach (var j in requirers)
                    {
                        if (j != i && adj[j].Add(i))
                        {
                            inDegree[i]++;
                        }
                    }
                }
            }
        }

        var ready = new SortedSet<int>();
        for (var i = 0; i < n; i++)
        {
            if (inDegree[i] == 0)
            {
                ready.Add(i);
            }
        }

        var result = new List<RunlistScriptEntry>(n);
        while (ready.Count > 0)
        {
            var next = ready.Min;
            ready.Remove(next);
            result.Add(orderedByTimestamp[next]);

            foreach (var dep in adj[next])
            {
                inDegree[dep]--;
                if (inDegree[dep] == 0)
                {
                    ready.Add(dep);
                }
            }
        }

        // If DDL statements themselves form a cycle, append remaining in timestamp order
        // so ValidateOrder() can report the exact DDL conflict.
        if (result.Count < n)
        {
            var seen = result.Select(r => r.Id).ToHashSet();
            foreach (var item in orderedByTimestamp)
            {
                if (seen.Add(item.Id))
                {
                    result.Add(item);
                }
            }
        }

        return result;
    }

    private static string ExtractTimestampPrefix(string scriptName)
    {
        var fileName = Path.GetFileName(scriptName);
        var withoutId = MigrationScriptHeader.StripIdPrefix(fileName);
        var idx = withoutId.IndexOf('_', StringComparison.Ordinal);
        if (idx >= 8 && withoutId[..idx].All(char.IsDigit))
        {
            return withoutId[..idx];
        }

        return "99999999999999";
    }

    private static ScriptDdlProfile AnalyzeStripped(string strippedSql)
    {
        var providesTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requiresTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dropsTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var providesObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requiresObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in CreateTableRegex().Matches(strippedSql))
        {
            providesTables.Add(NormalizeIdent(m.Groups[1].Value));
        }

        foreach (Match m in AlterTableRegex().Matches(strippedSql))
        {
            requiresTables.Add(NormalizeIdent(m.Groups[1].Value));
        }

        foreach (Match m in ReferencesRegex().Matches(strippedSql))
        {
            requiresTables.Add(NormalizeIdent(m.Groups[1].Value));
        }

        foreach (Match m in CreateIndexOnRegex().Matches(strippedSql))
        {
            requiresTables.Add(NormalizeIdent(m.Groups[1].Value));
        }

        foreach (Match m in DropTableRegex().Matches(strippedSql))
        {
            dropsTables.Add(NormalizeIdent(m.Groups[1].Value));
        }

        foreach (Match m in CreateSequenceRegex().Matches(strippedSql))
        {
            providesObjects.Add("seq:" + NormalizeIdent(m.Groups[1].Value));
        }

        foreach (Match m in NextValRegex().Matches(strippedSql))
        {
            requiresObjects.Add("seq:" + NormalizeIdent(m.Groups[1].Value));
        }

        foreach (Match m in CreateTypeRegex().Matches(strippedSql))
        {
            providesObjects.Add("type:" + NormalizeIdent(m.Groups[1].Value));
        }

        return new ScriptDdlProfile(
            providesTables,
            requiresTables,
            dropsTables,
            providesObjects,
            requiresObjects);
    }

    private static void PopulateTypeUsage(
        Dictionary<Guid, ScriptDdlProfile> profiles,
        Dictionary<Guid, string> strippedByScript,
        HashSet<string> allCreatedObjects)
    {
        var keysByShortName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in allCreatedObjects)
        {
            if (!obj.StartsWith("type:", StringComparison.Ordinal))
            {
                continue;
            }

            var typeName = obj["type:".Length..];
            var shortName = typeName.Contains('.', StringComparison.Ordinal)
                ? typeName[(typeName.LastIndexOf('.') + 1)..]
                : typeName;

            if (!keysByShortName.TryGetValue(shortName, out var keys))
            {
                keys = [];
                keysByShortName[shortName] = keys;
            }

            keys.Add(obj);
        }

        if (keysByShortName.Count == 0)
        {
            return;
        }

        var pattern = $@"(?<![\w""])(?:{string.Join("|", keysByShortName.Keys.Select(Regex.Escape))})(?![\w""])";
        var combinedTypeRegex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        foreach (var (scriptId, profile) in profiles)
        {
            if (!strippedByScript.TryGetValue(scriptId, out var stripped) || stripped.Length == 0)
            {
                continue;
            }

            foreach (Match match in combinedTypeRegex.Matches(stripped))
            {
                if (!keysByShortName.TryGetValue(match.Value, out var keys))
                {
                    continue;
                }

                foreach (var key in keys)
                {
                    if (!profile.ProvidesObjects.Contains(key))
                    {
                        profile.RequiresObjects.Add(key);
                    }
                }
            }
        }
    }

    private static string StripSqlComments(string sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return sql;
        }

        var sb = new System.Text.StringBuilder(sql.Length);
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];

            if (inSingleQuote)
            {
                sb.Append(c);
                if (c == '\'')
                {
                    if (i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        sb.Append('\'');
                        i++;
                    }
                    else
                    {
                        inSingleQuote = false;
                    }
                }

                continue;
            }

            if (inDoubleQuote)
            {
                sb.Append(c);
                if (c == '"')
                {
                    if (i + 1 < sql.Length && sql[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inDoubleQuote = false;
                    }
                }

                continue;
            }

            if (c == '\'')
            {
                inSingleQuote = true;
                sb.Append(c);
                continue;
            }

            if (c == '"')
            {
                inDoubleQuote = true;
                sb.Append(c);
                continue;
            }

            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                i += 2;
                while (i < sql.Length && sql[i] != '\n' && sql[i] != '\r')
                {
                    i++;
                }

                if (i < sql.Length)
                {
                    sb.Append(sql[i]);
                }

                continue;
            }

            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                {
                    i++;
                }

                if (i + 1 < sql.Length)
                {
                    i++;
                }

                sb.Append(' ');
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string NormalizeIdent(string raw)
    {
        var cleaned = raw.Replace("\"", string.Empty, StringComparison.Ordinal).Trim();
        if (cleaned.StartsWith("public.", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned["public.".Length..];
        }

        return cleaned;
    }

    [GeneratedRegex(@"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateTableRegex();

    [GeneratedRegex(@"ALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:ONLY\s+)?((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)", RegexOptions.IgnoreCase)]
    private static partial Regex AlterTableRegex();

    [GeneratedRegex(@"REFERENCES\s+((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencesRegex();

    [GeneratedRegex(@"CREATE\s+(?:UNIQUE\s+)?INDEX\s+(?:CONCURRENTLY\s+)?(?:IF\s+NOT\s+EXISTS\s+)?(?:""[^""]+""|\w+)\s+ON\s+((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateIndexOnRegex();

    [GeneratedRegex(@"DROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)", RegexOptions.IgnoreCase)]
    private static partial Regex DropTableRegex();

    [GeneratedRegex(@"CREATE\s+(?:OR\s+REPLACE\s+)?SEQUENCE\s+(?:IF\s+NOT\s+EXISTS\s+)?((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateSequenceRegex();

    [GeneratedRegex(@"nextval\s*\(\s*'([^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex NextValRegex();

    [GeneratedRegex(@"CREATE\s+TYPE\s+((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateTypeRegex();
}
