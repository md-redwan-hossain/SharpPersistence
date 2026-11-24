using System.Reflection;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed class MigrationDoctor
{
    private readonly MigrationManagerOptions _options;
    private readonly MigrationScriptStore _scriptStore;
    private readonly DoctorWiringValidator _wiringValidator;
    private readonly DoctorEntityValidator _entityValidator;
    private readonly DoctorCrossRegistryValidator _crossRegistryValidator;
    private readonly DoctorDatabaseValidator _databaseValidator;

    public MigrationDoctor(
        MigrationManagerOptions options,
        IMigrationDialect dialect,
        MigrationConnectionOpener connectionOpener,
        MigrationScriptStore scriptStore,
        MigrationHistoryStore historyStore,
        SchemaDriftVerifier driftVerifier)
    {
        _options = options;
        _scriptStore = scriptStore;
        _wiringValidator = new DoctorWiringValidator(options, dialect, connectionOpener);
        _entityValidator = new DoctorEntityValidator(options, dialect);
        _crossRegistryValidator = new DoctorCrossRegistryValidator(options, dialect);
        _databaseValidator = new DoctorDatabaseValidator(
            options,
            dialect,
            connectionOpener,
            scriptStore,
            historyStore,
            driftVerifier);
    }

    public async Task<DoctorReport> DiagnoseAsync(
        string? registryFilter,
        bool checkDatabase,
        IReadOnlyList<Assembly>? discoveredAssemblies,
        CancellationToken ct)
    {
        var wiringChecks = _wiringValidator.Validate(registryFilter, discoveredAssemblies);
        var registryReports = new List<DoctorRegistryReport>();
        var crossRegistryChecks = new List<DoctorCheckItem>();
        var migrationCoverageChecks = new List<DoctorCheckItem>();

        var targetRegistries = string.IsNullOrWhiteSpace(registryFilter)
            ? _options.Registries
            : _options.Registries
                .Where(r => string.Equals(r.GetType().Name, registryFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var (allTableDefinitions, evaluatedRegistries) = ScanAllRegistriesSafely(crossRegistryChecks);

        var enumLookup = _options.Enums.ToDictionary(e => e.ClrTypeName, StringComparer.Ordinal);
        var targetRegistryNames = targetRegistries
            .Select(r => r.GetType().Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (regName, evaluated, items) in evaluatedRegistries)
        {
            if (!targetRegistryNames.Contains(regName))
            {
                continue;
            }

            var entityCount = 0;
            var excludedEntityCount = 0;
            var columnCount = 0;
            var fkCount = 0;
            var constraintCount = 0;

            if (evaluated is not null)
            {
                foreach (var entityType in evaluated.Mappings.Keys)
                {
                    if (!allTableDefinitions.TryGetValue(entityType, out var entry) ||
                        !string.Equals(entry.RegistryName, regName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var tb = entry.Table;
                    entityCount++;
                    if (tb.ExcludeFromMigrations)
                    {
                        excludedEntityCount++;
                    }
                    columnCount += tb.ColumnsByProp.Count;
                    fkCount += tb.ForeignKeys.Count;
                    constraintCount += tb.CheckConstraints.Count + tb.ExclusionConstraints.Count + tb.Indexes.Count;

                    _entityValidator.ValidateTable(tb, allTableDefinitions, enumLookup, items);
                }
            }

            if (items.Count == 0)
            {
                items.Add(new DoctorCheckItem(
                    DoctorSeverity.Pass,
                    $"All {entityCount} entities, {columnCount} columns, {fkCount} foreign keys, and {constraintCount} constraints are valid"));
            }

            registryReports.Add(new DoctorRegistryReport(
                regName,
                entityCount,
                excludedEntityCount,
                columnCount,
                fkCount,
                constraintCount,
                items));
        }

        _crossRegistryValidator.Validate(
            allTableDefinitions,
            crossRegistryChecks,
            registryReports.Any(r => r.HasErrors));

        var hasSchemaErrors = registryReports.Any(r => r.HasErrors) ||
                              crossRegistryChecks.Any(c => c.Severity == DoctorSeverity.Error);

        if (!hasSchemaErrors && targetRegistries.Count > 0)
        {
            await RunMigrationCoverageChecksAsync(targetRegistries, migrationCoverageChecks, ct);
        }

        var databaseChecks = checkDatabase
            ? await _databaseValidator.ValidateAsync(registryFilter, hasSchemaErrors, ct)
            : [];

        return new DoctorReport(
            _options.Dialect,
            checkDatabase,
            wiringChecks,
            registryReports,
            crossRegistryChecks,
            migrationCoverageChecks,
            databaseChecks);
    }

    private (
        Dictionary<Type, (string RegistryName, TableDefinition Table)> AllTables,
        List<(string Name, EntityRegistryBuilder? Evaluated, List<DoctorCheckItem> Items)> EvaluatedRegistries)
        ScanAllRegistriesSafely(List<DoctorCheckItem> crossRegistryChecks)
    {
        var nullabilityContext = new NullabilityInfoContext();
        var allTableDefinitions = new Dictionary<Type, (string RegistryName, TableDefinition Table)>();
        var evaluatedRegistries = new List<(string Name, EntityRegistryBuilder? Evaluated, List<DoctorCheckItem> Items)>();

        foreach (var registry in _options.Registries)
        {
            var regName = registry.GetType().Name;
            var items = new List<DoctorCheckItem>();
            EntityRegistryBuilder? evaluated;

            try
            {
                evaluated = EntityRegistryBuilder.Evaluate(registry);
            }
            catch (Exception ex)
            {
                var root = UnwrapException(ex);
                items.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Registry '{regName}' threw {root.GetType().Name} during Configure(): {root.Message}"));
                evaluatedRegistries.Add((regName, null, items));
                continue;
            }

            var registrySchema = string.IsNullOrWhiteSpace(evaluated.DefaultSchema) ? null : evaluated.DefaultSchema;
            foreach (var (entityType, configType) in evaluated.Mappings)
            {
                if (allTableDefinitions.TryGetValue(entityType, out var existing))
                {
                    crossRegistryChecks.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Entity '{entityType.Name}' is mapped in both '{existing.RegistryName}' and '{regName}'.",
                        Hint: "Map each entity in only one IEntityRegistry."));
                    continue;
                }

                try
                {
                    var tableDef = SchemaScanner.BuildSingleTableDefinition(
                        entityType,
                        configType,
                        evaluated.InlineConfigurations.TryGetValue(entityType, out var inlineActions) ? inlineActions : null,
                        registrySchema,
                        _options.Dialect,
                        _options.IdentifierCase,
                        _options.PropertyConventions,
                        _options.SqlExpressionOptions,
                        nullabilityContext,
                        _options.PluralizeTableNames,
                        _options.EntityConventions,
                        _options.TableNameConvention);

                    allTableDefinitions[entityType] = (regName, tableDef);
                }
                catch (Exception ex)
                {
                    var root = UnwrapException(ex);
                    items.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Configuration failed ({root.GetType().Name}): {root.Message}",
                        EntityName: entityType.Name));
                }
            }

            evaluatedRegistries.Add((regName, evaluated, items));
        }

        return (allTableDefinitions, evaluatedRegistries);
    }

    private async Task RunMigrationCoverageChecksAsync(
        IReadOnlyList<IEntityRegistry> targetRegistries,
        List<DoctorCheckItem> checks,
        CancellationToken ct)
    {
        var stateDir = _scriptStore.ResolveSnapshotDirectory();
        var totalPendingChanges = 0;

        foreach (var registry in targetRegistries)
        {
            var registryName = registry.GetType().Name;
            var snapshotPath = Path.Combine(stateDir, $"{registryName}.json");
            var previousJson = File.Exists(snapshotPath)
                ? await File.ReadAllTextAsync(snapshotPath, ct)
                : null;

            var currentSchema = SchemaScanner.ScanRegistry(registry, _options);

            totalPendingChanges += StandaloneSchemaDiffer.CountPendingOperations(currentSchema, previousJson, _options.Dialect);
        }

        if (totalPendingChanges == 0)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Pass,
                "All entity changes are covered by migration scripts"));
        }
        else
        {
            var msg = totalPendingChanges == 1
                ? "1 entity change is not in a migration script yet"
                : $"{totalPendingChanges} entity changes are not in a migration script yet";

            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Warning,
                msg,
                Hint: "Run `db-migrator generate --name <Name>`"));
        }
    }

    internal static Exception UnwrapException(Exception ex) =>
        ex is TargetInvocationException tie && tie.InnerException is not null
            ? tie.InnerException
            : ex;
}
