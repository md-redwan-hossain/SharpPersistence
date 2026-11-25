using System.Text.Json;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed record RunlistSyncResult(
    int StampedCount,
    int AddedCount,
    int PrunedCount,
    string RunlistPath,
    bool WasWritten,
    IReadOnlyList<Guid>? StaleOverrideIds = null);

internal sealed class MigrationRunlistManager
{
    public const string RunlistFileName = "db-migration-runlist.json";
    public const string OverridesFileName = "db-migration-runlist.overrides.json";

    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions ReadOverridesJsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly DbMigratorOptions _options;
    private readonly MigrationScriptStore _scriptStore;

    public MigrationRunlistManager(DbMigratorOptions options, MigrationScriptStore scriptStore)
    {
        _options = options;
        _scriptStore = scriptStore;
    }

    public string RunlistPath => Path.Combine(_scriptStore.ResolveMigrationsDirectory(), RunlistFileName);
    public string OverridesPath => Path.Combine(_scriptStore.ResolveMigrationsDirectory(), OverridesFileName);

    public IReadOnlyList<RunlistScriptEntry> ResolveEffectiveOrder()
    {
        var (scriptsById, missingHeaders, duplicateIds) = ScanDiskScripts();
        if (scriptsById.Count == 0 && missingHeaders.Count == 0)
        {
            return [];
        }

        if (missingHeaders.Count > 0)
        {
            throw new InvalidOperationException(
                $"Migration script(s) missing '{MigrationScriptHeader.HeaderPrefix}' header: {string.Join(", ", missingHeaders)}. " +
                "Run 'db-migrator runlist sync' to stamp IDs and update the runlist.");
        }

        if (duplicateIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate migration-script-id detected: {string.Join(", ", duplicateIds)}.");
        }

        if (!File.Exists(RunlistPath))
        {
            throw new InvalidOperationException(
                $"Migration runlist '{RunlistFileName}' was not found in '{_scriptStore.ResolveMigrationsDirectory()}'. " +
                "Run 'db-migrator runlist sync' to generate it.");
        }

        var runlistIds = ReadRunlistIds(RunlistPath);
        var missingOnDisk = runlistIds.Where(id => !scriptsById.ContainsKey(id)).ToList();
        if (missingOnDisk.Count > 0)
        {
            throw new InvalidOperationException(
                $"'{RunlistFileName}' references missing script ID(s): {string.Join(", ", missingOnDisk)}. " +
                "Restore the missing file(s) or run 'db-migrator runlist sync --prune'.");
        }

        var listedSet = runlistIds.ToHashSet();
        var untracked = scriptsById.Values
            .Where(s => !listedSet.Contains(s.Id))
            .Select(s => $"{s.RegistryName}/{s.ScriptName} ({s.Id})")
            .ToList();
        if (untracked.Count > 0)
        {
            throw new InvalidOperationException(
                $"Untracked migration script(s) found on disk: {string.Join(", ", untracked)}. " +
                "Run 'db-migrator runlist sync' to add them to the runlist.");
        }

        var baseOrder = runlistIds.Select(id => scriptsById[id]).ToList();
        var overrides = ReadAndValidateOverrides(scriptsById);
        return ApplyOverrides(baseOrder, overrides);
    }

    public void EnsureReadyForAppend()
    {
        if (File.Exists(RunlistPath))
        {
            return;
        }

        if (EnumerateSqlFiles().Count > 0)
        {
            throw new InvalidOperationException(
                $"Existing migration scripts were found in '{_scriptStore.ResolveMigrationsDirectory()}', " +
                $"but '{RunlistFileName}' is missing. Run 'db-migrator runlist sync' before generating new migrations.");
        }
    }

    public async Task AppendNewScriptsAsync(IReadOnlyList<Guid> newIds, CancellationToken ct)
    {
        if (newIds.Count == 0)
        {
            return;
        }

        var existingIds = File.Exists(RunlistPath)
            ? ReadRunlistIds(RunlistPath)
            : [];

        var seen = existingIds.ToHashSet();
        var updated = new List<Guid>(existingIds.Count + newIds.Count);
        updated.AddRange(existingIds);

        foreach (var id in newIds)
        {
            if (seen.Add(id))
            {
                updated.Add(id);
            }
        }

        var dir = Path.GetDirectoryName(RunlistPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(updated, WriteJsonOptions);
        await File.WriteAllTextAsync(RunlistPath, json, ct).ConfigureAwait(false);
    }

    public async Task<RunlistSyncResult> SyncAsync(
        bool prune = false,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        var diskFiles = EnumerateSqlFiles();
        var stampedCount = 0;
        var scriptsById = new Dictionary<Guid, RunlistScriptEntry>();
        var duplicateIds = new List<string>();

        foreach (var (registryName, scriptName, fullPath) in diskFiles)
        {
            var sql = await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
            var (stampedSql, id, alreadyHad) = MigrationScriptHeader.StampHeader(sql);
            if (!alreadyHad)
            {
                stampedCount++;
                if (!dryRun)
                {
                    await File.WriteAllTextAsync(fullPath, stampedSql, ct).ConfigureAwait(false);
                }
            }

            if (!scriptsById.TryAdd(id, new RunlistScriptEntry(id, registryName, scriptName, fullPath)))
            {
                duplicateIds.Add($"{id} ({registryName}/{scriptName})");
            }
        }

        if (duplicateIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate migration-script-id detected: {string.Join(", ", duplicateIds)}.");
        }

        var allEntries = new List<RunlistScriptEntry>(scriptsById.Values);
        var existingIds = File.Exists(RunlistPath)
            ? ReadRunlistIds(RunlistPath)
            : [];

        var prunedCount = 0;
        var preserved = new List<Guid>(existingIds.Count);
        var preservedSet = new HashSet<Guid>();

        foreach (var id in existingIds)
        {
            if (!scriptsById.ContainsKey(id))
            {
                if (prune)
                {
                    prunedCount++;
                    continue;
                }
            }

            if (preservedSet.Add(id))
            {
                preserved.Add(id);
            }
        }

        var untracked = allEntries.Where(e => !preservedSet.Contains(e.Id)).ToList();
        var sortedRegistries = RegistryDependencyResolver.SortTopologically(_options);
        var sortedUntracked = SqlScriptDependencyAnalyzer.SortByDependenciesAndTimestamp(untracked, sortedRegistries);

        var finalIds = new List<Guid>(preserved.Count + sortedUntracked.Count);
        finalIds.AddRange(preserved);
        finalIds.AddRange(sortedUntracked.Select(u => u.Id));

        if (!dryRun && (finalIds.Count > 0 || File.Exists(RunlistPath)))
        {
            var dir = Path.GetDirectoryName(RunlistPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(finalIds, WriteJsonOptions);
            await File.WriteAllTextAsync(RunlistPath, json, ct).ConfigureAwait(false);
        }

        var staleOverrideIds = FindStaleOverrideIds(scriptsById);

        return new RunlistSyncResult(
            stampedCount,
            sortedUntracked.Count,
            prunedCount,
            RunlistPath,
            !dryRun && (finalIds.Count > 0 || File.Exists(RunlistPath)),
            staleOverrideIds);
    }

    private List<Guid> FindStaleOverrideIds(IReadOnlyDictionary<Guid, RunlistScriptEntry> scriptsById)
    {
        if (!File.Exists(OverridesPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(OverridesPath);
            var rawSequences = JsonSerializer.Deserialize<List<List<string>>>(json, ReadOverridesJsonOptions);
            if (rawSequences is null)
            {
                return [];
            }

            var stale = new List<Guid>();
            var seen = new HashSet<Guid>();
            foreach (var seq in rawSequences)
            {
                if (seq is null)
                {
                    continue;
                }

                foreach (var rawId in seq)
                {
                    if (Guid.TryParse(rawId, out var id) && id != Guid.Empty && !scriptsById.ContainsKey(id) && seen.Add(id))
                    {
                        stale.Add(id);
                    }
                }
            }

            return stale;
        }
        catch
        {
            return [];
        }
    }

    public List<DoctorCheckItem> Validate(
        IReadOnlyList<AppliedMigrationRecord>? applied = null,
        bool includeDdlValidation = true)
    {
        var checks = new List<DoctorCheckItem>();
        var (scriptsById, missingHeaders, duplicateIds) = ScanDiskScripts();

        if (scriptsById.Count == 0 && missingHeaders.Count == 0 && !File.Exists(RunlistPath))
        {
            return checks;
        }

        if (missingHeaders.Count > 0)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                $"{missingHeaders.Count} migration script(s) missing '{MigrationScriptHeader.HeaderPrefix}' header: {string.Join(", ", missingHeaders)}",
                Hint: "Run 'db-migrator runlist sync' to stamp headers."));
        }

        if (duplicateIds.Count > 0)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                $"Duplicate migration-script-id found: {string.Join(", ", duplicateIds)}",
                Hint: "Ensure every .sql file has a unique migration-script-id."));
        }

        if (!File.Exists(RunlistPath))
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                $"Missing '{RunlistFileName}' while {scriptsById.Count + missingHeaders.Count} migration script(s) exist on disk.",
                Hint: "Run 'db-migrator runlist sync' to generate db-migration-runlist.json."));
            return checks;
        }

        List<Guid> runlistIds;
        try
        {
            runlistIds = ReadRunlistIds(RunlistPath);
        }
        catch (Exception ex)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                $"Invalid '{RunlistFileName}': {ex.Message}",
                Hint: "Fix the JSON array or regenerate it with 'db-migrator runlist sync'."));
            return checks;
        }

        var missingFiles = runlistIds.Where(id => !scriptsById.ContainsKey(id)).ToList();
        if (missingFiles.Count > 0)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                $"'{RunlistFileName}' references {missingFiles.Count} missing script ID(s): {string.Join(", ", missingFiles)}",
                Hint: "Restore the deleted script(s) or run 'db-migrator runlist sync --prune'."));
        }

        var listedSet = runlistIds.ToHashSet();
        var untracked = scriptsById.Values.Where(s => !listedSet.Contains(s.Id)).ToList();
        if (untracked.Count > 0)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                $"{untracked.Count} migration script(s) on disk are not in '{RunlistFileName}': {string.Join(", ", untracked.Select(u => $"{u.RegistryName}/{u.ScriptName}"))}",
                Hint: "Run 'db-migrator runlist sync' to add them."));
        }

        List<List<Guid>> overrides;
        try
        {
            overrides = ReadAndValidateOverrides(scriptsById);
        }
        catch (Exception ex)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                ex.Message,
                Hint: $"Fix '{OverridesFileName}'."));
            return checks;
        }

        if (missingHeaders.Count == 0 && duplicateIds.Count == 0 && missingFiles.Count == 0 && untracked.Count == 0)
        {
            try
            {
                var baseOrder = runlistIds.Select(id => scriptsById[id]).ToList();
                var effectiveOrder = ApplyOverrides(baseOrder, overrides);

                if (includeDdlValidation)
                {
                    var ddlChecks = SqlScriptDependencyAnalyzer.ValidateOrder(effectiveOrder);
                    checks.AddRange(ddlChecks);
                }

                if (applied is { Count: > 0 })
                {
                    var appliedSet = applied
                        .Select(a => (a.RegistryName, a.ScriptName))
                        .ToHashSet();

                    var seenUnapplied = false;
                    string? firstUnapplied = null;
                    foreach (var s in effectiveOrder)
                    {
                        var isApplied = appliedSet.Contains((s.RegistryName, s.ScriptName));
                        if (!isApplied)
                        {
                            seenUnapplied = true;
                            firstUnapplied ??= $"{s.RegistryName}/{s.ScriptName}";
                        }
                        else if (seenUnapplied)
                        {
                            checks.Add(new DoctorCheckItem(
                                DoctorSeverity.Warning,
                                $"Already-applied script '{s.RegistryName}/{s.ScriptName}' is ordered after unapplied script '{firstUnapplied}'.",
                                Hint: "Review db-migration-runlist.overrides.json so new migrations are not inserted before applied ones."));
                            break;
                        }
                    }
                }

                if (checks.Count == 0)
                {
                    var overrideNote = overrides.Count > 0 ? $" ({overrides.Count} override sequence(s))" : string.Empty;
                    checks.Add(new DoctorCheckItem(
                        DoctorSeverity.Pass,
                        $"Migration runlist verified ({effectiveOrder.Count} scripts{overrideNote})"));
                }
            }
            catch (Exception ex)
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    ex.Message,
                    Hint: $"Check '{OverridesFileName}' for conflicting sequences."));
            }
        }

        return checks;
    }

    private (
        Dictionary<Guid, RunlistScriptEntry> ScriptsById,
        List<string> MissingHeaders,
        List<string> DuplicateIds) ScanDiskScripts()
    {
        var files = EnumerateSqlFiles();
        var scriptsById = new Dictionary<Guid, RunlistScriptEntry>();
        var missingHeaders = new List<string>();
        var duplicateIds = new List<string>();

        foreach (var (registryName, scriptName, fullPath) in files)
        {
            if (!MigrationScriptHeader.TryParseIdFromFile(fullPath, out var id))
            {
                missingHeaders.Add($"{registryName}/{scriptName}");
                continue;
            }

            if (!scriptsById.TryAdd(id, new RunlistScriptEntry(id, registryName, scriptName, fullPath)))
            {
                duplicateIds.Add($"{id} ({registryName}/{scriptName})");
            }
        }

        return (scriptsById, missingHeaders, duplicateIds);
    }

    private List<(string RegistryName, string ScriptName, string FullPath)> EnumerateSqlFiles()
    {
        var migrationsRoot = _scriptStore.ResolveMigrationsDirectory();
        if (!Directory.Exists(migrationsRoot))
        {
            return [];
        }

        var results = new List<(string, string, string)>();
        var dirs = Directory.GetDirectories(migrationsRoot)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal);

        foreach (var dir in dirs)
        {
            var folder = Path.GetFileName(dir);
            var files = Directory.GetFiles(dir, "*.sql", SearchOption.AllDirectories)
                .OrderBy(f => Path.GetRelativePath(dir, f).Replace('\\', '/'), StringComparer.Ordinal);
            foreach (var file in files)
            {
                var relScript = Path.GetRelativePath(dir, file).Replace('\\', '/');
                results.Add((folder, relScript, file));
            }
        }

        return results;
    }

    private static List<Guid> ReadRunlistIds(string path)
    {
        var json = File.ReadAllText(path);
        var raw = JsonSerializer.Deserialize<List<string>>(json)
            ?? throw new InvalidOperationException($"'{RunlistFileName}' must be a JSON array of script GUIDs.");

        var ids = new List<Guid>(raw.Count);
        var seen = new HashSet<Guid>();
        for (var i = 0; i < raw.Count; i++)
        {
            if (!Guid.TryParse(raw[i], out var id) || id == Guid.Empty)
            {
                throw new InvalidOperationException(
                    $"Invalid migration-script-id '{raw[i]}' at index {i} in '{RunlistFileName}'.");
            }

            if (!seen.Add(id))
            {
                throw new InvalidOperationException(
                    $"Duplicate migration-script-id '{id}' in '{RunlistFileName}'.");
            }

            ids.Add(id);
        }

        return ids;
    }

    private List<List<Guid>> ReadAndValidateOverrides(IReadOnlyDictionary<Guid, RunlistScriptEntry> scriptsById)
    {
        if (!File.Exists(OverridesPath))
        {
            return [];
        }

        var json = File.ReadAllText(OverridesPath);
        var rawSequences = JsonSerializer.Deserialize<List<List<string>>>(json, ReadOverridesJsonOptions)
            ?? throw new InvalidOperationException($"'{OverridesFileName}' must be a JSON array of ID sequences.");

        var parsed = new List<List<Guid>>(rawSequences.Count);
        for (var seqIdx = 0; seqIdx < rawSequences.Count; seqIdx++)
        {
            var seq = rawSequences[seqIdx];
            if (seq is null || seq.Count < 2)
            {
                throw new InvalidOperationException(
                    $"Override sequence at index {seqIdx} in '{OverridesFileName}' must contain at least 2 script IDs.");
            }

            var guidSeq = new List<Guid>(seq.Count);
            var seenInSeq = new HashSet<Guid>();
            for (var itemIdx = 0; itemIdx < seq.Count; itemIdx++)
            {
                var rawId = seq[itemIdx];
                if (!Guid.TryParse(rawId, out var id) || id == Guid.Empty)
                {
                    throw new InvalidOperationException(
                        $"Invalid migration-script-id '{rawId}' in override sequence [{seqIdx}][{itemIdx}] of '{OverridesFileName}'.");
                }

                if (!scriptsById.ContainsKey(id))
                {
                    throw new InvalidOperationException(
                        $"Override sequence [{seqIdx}][{itemIdx}] in '{OverridesFileName}' references non-existent script ID '{id}'.");
                }

                if (!seenInSeq.Add(id))
                {
                    throw new InvalidOperationException(
                        $"Override sequence [{seqIdx}] in '{OverridesFileName}' contains duplicate script ID '{id}'.");
                }

                guidSeq.Add(id);
            }

            parsed.Add(guidSeq);
        }

        return parsed;
    }

    private static List<RunlistScriptEntry> ApplyOverrides(
        IReadOnlyList<RunlistScriptEntry> baseOrder,
        IReadOnlyList<List<Guid>> overrides)
    {
        if (overrides.Count == 0 || baseOrder.Count <= 1)
        {
            return [.. baseOrder];
        }

        var n = baseOrder.Count;
        var indexById = new Dictionary<Guid, int>(n);
        for (var i = 0; i < n; i++)
        {
            indexById[baseOrder[i].Id] = i;
        }

        var overriddenIds = new HashSet<Guid>();
        var adj = new HashSet<int>[n];
        var inDegree = new int[n];
        for (var i = 0; i < n; i++)
        {
            adj[i] = [];
        }

        // Strict override edges
        foreach (var seq in overrides)
        {
            for (var i = 0; i < seq.Count; i++)
            {
                overriddenIds.Add(seq[i]);
                if (i + 1 < seq.Count)
                {
                    var fromIdx = indexById[seq[i]];
                    var toIdx = indexById[seq[i + 1]];
                    if (adj[fromIdx].Add(toIdx))
                    {
                        inDegree[toIdx]++;
                    }
                }
            }
        }

        // Preserve relative base order (i -> i+1) whenever it does not conflict with explicit override edges
        var visitedGen = new int[n];
        var currentGen = 0;
        var stack = new Stack<int>();
        for (var i = 0; i + 1 < n; i++)
        {
            var canReachBack = adj[i + 1].Count > 0 && CanReach(adj, i + 1, i, visitedGen, ++currentGen, stack);
            if (!canReachBack && adj[i].Add(i + 1))
            {
                inDegree[i + 1]++;
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
            var entry = baseOrder[next];
            result.Add(overriddenIds.Contains(entry.Id) ? entry with { IsOverridden = true } : entry);

            foreach (var dep in adj[next])
            {
                inDegree[dep]--;
                if (inDegree[dep] == 0)
                {
                    ready.Add(dep);
                }
            }
        }

        if (result.Count < n)
        {
            throw new InvalidOperationException(
                $"Conflicting or cyclic override sequences detected in '{OverridesFileName}'.");
        }

        return result;
    }

    private static bool CanReach(
        HashSet<int>[] adj,
        int start,
        int target,
        int[] visitedGen,
        int gen,
        Stack<int> stack)
    {
        if (start == target)
        {
            return true;
        }

        stack.Clear();
        stack.Push(start);
        visitedGen[start] = gen;

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            foreach (var next in adj[node])
            {
                if (next == target)
                {
                    return true;
                }

                if (visitedGen[next] != gen)
                {
                    visitedGen[next] = gen;
                    stack.Push(next);
                }
            }
        }

        return false;
    }
}
