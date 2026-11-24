using System.Reflection;
using Humanizer;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal static class SchemaScanner
{
    public static IReadOnlyList<IEntityRegistry> DiscoverRegistries(params Assembly[] assemblies)
    {
        var registries = new List<IEntityRegistry>();

        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface || !type.IsClass || !typeof(IEntityRegistry).IsAssignableFrom(type))
                {
                    continue;
                }

                if (Activator.CreateInstance(type) is IEntityRegistry registry)
                {
                    registries.Add(registry);
                }
            }
        }

        return registries;
    }

    public static DatabaseSchema ScanAllRegistries(
        SqlDialect dialect,
        SqlIdentifierCase identifierCase,
        IReadOnlyList<PropertyConventionRule>? propertyConventions = null,
        IReadOnlyList<PostgresEnumSchema>? pgEnums = null,
        CheckConstraintBuilderOptions? checkConstraintOptions = null,
        params Assembly[] assemblies)
    {
        var registries = DiscoverRegistries(assemblies);
        return ScanRegistries(dialect, identifierCase, propertyConventions, pgEnums, checkConstraintOptions, false, [.. registries]);
    }

    public static DatabaseSchema ScanRegistries(
        SqlDialect dialect,
        SqlIdentifierCase identifierCase,
        IReadOnlyList<PropertyConventionRule>? propertyConventions = null,
        IReadOnlyList<PostgresEnumSchema>? pgEnums = null,
        CheckConstraintBuilderOptions? checkConstraintOptions = null,
        params IEntityRegistry[] registries) =>
        ScanRegistries(dialect, identifierCase, propertyConventions, pgEnums, checkConstraintOptions, false, registries);

    public static DatabaseSchema ScanRegistries(
        SqlDialect dialect,
        SqlIdentifierCase identifierCase,
        IReadOnlyList<PropertyConventionRule>? propertyConventions,
        IReadOnlyList<PostgresEnumSchema>? pgEnums,
        CheckConstraintBuilderOptions? checkConstraintOptions,
        bool pluralizeTableNames,
        params IEntityRegistry[] registries)
    {
        var (buildersByType, sequences) = BuildTableBuilders(registries, dialect, identifierCase, propertyConventions, checkConstraintOptions, pluralizeTableNames);
        return SchemaFinalizer.FinalizeSchema(buildersByType, includedEntityTypes: null, sequences, pgEnums);
    }

    public static DatabaseSchema ScanRegistry(
        IEntityRegistry registry,
        SqlDialect dialect,
        SqlIdentifierCase identifierCase,
        IReadOnlyList<IEntityRegistry>? allRegistries = null,
        IReadOnlyList<PropertyConventionRule>? propertyConventions = null,
        IReadOnlyList<PostgresEnumSchema>? pgEnums = null,
        CheckConstraintBuilderOptions? checkConstraintOptions = null,
        bool pluralizeTableNames = false)
    {
        var contextRegistries = allRegistries is { Count: > 0 }
            ? allRegistries
            : [registry];

        var evaluated = EntityRegistryBuilder.Evaluate(registry);
        var (buildersByType, _) = BuildTableBuilders(contextRegistries, dialect, identifierCase, propertyConventions, checkConstraintOptions, pluralizeTableNames);
        var ownedTypes = evaluated.Mappings.Keys.ToHashSet();
        var ownedSequences = BuildSequenceSchemas(evaluated, dialect);

        return SchemaFinalizer.FinalizeSchema(buildersByType, ownedTypes, ownedSequences, pgEnums);
    }

    private static (Dictionary<Type, TableDefinition> Builders, List<SequenceSchema> Sequences) BuildTableBuilders(
        IReadOnlyList<IEntityRegistry> registries,
        SqlDialect dialect,
        SqlIdentifierCase identifierCase,
        IReadOnlyList<PropertyConventionRule>? propertyConventions,
        CheckConstraintBuilderOptions? checkConstraintOptions,
        bool pluralizeTableNames)
    {
        var buildersByType = new Dictionary<Type, TableDefinition>();
        var sequences = new List<SequenceSchema>();
        var nullabilityContext = new NullabilityInfoContext();

        foreach (var registry in registries)
        {
            var evaluated = EntityRegistryBuilder.Evaluate(registry);
            sequences.AddRange(BuildSequenceSchemas(evaluated, dialect));
            var registrySchema = string.IsNullOrWhiteSpace(evaluated.DefaultSchema) ? null : evaluated.DefaultSchema;

            foreach (var (entityType, configType) in evaluated.Mappings)
            {
                if (buildersByType.ContainsKey(entityType))
                {
                    throw new InvalidOperationException(
                        $"Entity '{entityType.FullName ?? entityType.Name}' is mapped in multiple registries.");
                }

                var tableBuilder = BuildSingleTableDefinition(
                    entityType,
                    configType,
                    evaluated.InlineConfigurations.TryGetValue(entityType, out var inlineActions) ? inlineActions : null,
                    registrySchema,
                    dialect,
                    identifierCase,
                    propertyConventions,
                    checkConstraintOptions,
                    nullabilityContext,
                    pluralizeTableNames);

                buildersByType[entityType] = tableBuilder;
            }
        }

        return (buildersByType, sequences);
    }

    internal static TableDefinition BuildSingleTableDefinition(
        Type entityType,
        Type? configType,
        IReadOnlyList<Action<object>>? inlineActions,
        string? defaultSchema,
        SqlDialect dialect,
        SqlIdentifierCase identifierCase,
        IReadOnlyList<PropertyConventionRule>? propertyConventions,
        CheckConstraintBuilderOptions? checkConstraintOptions,
        NullabilityInfoContext? nullabilityContext = null,
        bool pluralizeTableNames = false)
    {
        nullabilityContext ??= new NullabilityInfoContext();
        var rawTableName = pluralizeTableNames
            ? entityType.Name.Pluralize(inputIsKnownToBeSingular: false)
            : entityType.Name;
        var defaultTableName = NamingFormatter.Format(rawTableName, identifierCase);

        var tableBuilder = new TableDefinition(
            entityType,
            defaultTableName,
            defaultSchema,
            dialect,
            identifierCase,
            checkConstraintOptions);

        foreach (var prop in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite || !IsScalarType(prop.PropertyType))
            {
                continue;
            }

            var nullability = nullabilityContext.Create(prop);
            var isNullable = nullability.ReadState == NullabilityState.Nullable
                             || Nullable.GetUnderlyingType(prop.PropertyType) is not null;

            var col = tableBuilder.GetOrAddColumn(prop.Name, prop.PropertyType);
            col.ColumnName = NamingFormatter.Format(prop.Name, identifierCase);
            col.IsNullable = isNullable;

            ApplyGlobalPropertyConventions(col, prop.PropertyType, propertyConventions);

            if (string.Equals(prop.Name, "Id", StringComparison.OrdinalIgnoreCase))
            {
                tableBuilder.SetPrimaryKey([prop.Name], null);
            }
        }

        if (configType is not null)
        {
            InvokeEntityConfig(entityType, configType, tableBuilder);
        }

        if (inlineActions is not null)
        {
            var entityBuilder = CreateEntityBuilder(entityType, tableBuilder);
            if (entityBuilder is not null)
            {
                foreach (var action in inlineActions)
                {
                    action(entityBuilder);
                }
            }
        }

        return tableBuilder;
    }

    private static List<SequenceSchema> BuildSequenceSchemas(EntityRegistryBuilder evaluated, SqlDialect dialect)
    {
        var defaultSchema = string.IsNullOrWhiteSpace(evaluated.DefaultSchema) ? null : evaluated.DefaultSchema;
        var list = new List<SequenceSchema>(evaluated.Sequences.Count);

        foreach (var seq in evaluated.Sequences)
        {
            var t = Nullable.GetUnderlyingType(seq.ClrType) ?? seq.ClrType;
            var clrName = t == typeof(short) ? "short" : t == typeof(int) ? "int" : "long";
            var sqlType = dialect switch
            {
                SqlDialect.PostgreSql => t == typeof(short) ? "smallint" : t == typeof(int) ? "integer" : "bigint",
                SqlDialect.Oracle => "NUMBER",
                _ => throw new NotSupportedException($"RDBMS '{dialect}' is not supported yet.")
            };

            list.Add(new SequenceSchema
            {
                Name = seq.Name,
                Schema = seq.SchemaExplicitlySet ? seq.Schema : defaultSchema,
                ClrTypeName = clrName,
                SqlType = sqlType,
                StartValue = seq.StartValue,
                IncrementBy = seq.IncrementBy,
                MinValue = seq.MinValue,
                MaxValue = seq.MaxValue,
                IsCyclic = seq.IsCyclic
            });
        }

        return list;
    }

    private static void ApplyGlobalPropertyConventions(
        ColumnDefinition col,
        Type clrType,
        IReadOnlyList<PropertyConventionRule>? conventions)
    {
        if (conventions is null or { Count: 0 })
        {
            return;
        }

        var unwrapped = Nullable.GetUnderlyingType(clrType) ?? clrType;
        var columnBuilder = new ColumnBuilder(col);

        foreach (var rule in conventions)
        {
            var matches = rule.TargetClrType == clrType
                          || rule.TargetClrType == unwrapped
                          || (rule.TargetClrType == typeof(Enum) && unwrapped.IsEnum);

            if (matches)
            {
                rule.Configure(columnBuilder);
            }
        }
    }

    private static object? CreateEntityBuilder(Type entityType, TableDefinition tableBuilder)
    {
        var entityBuilderType = typeof(EntityBuilder<>).MakeGenericType(entityType);
        return Activator.CreateInstance(
            entityBuilderType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: [tableBuilder],
            culture: null);
    }

    private static void InvokeEntityConfig(Type entityType, Type configType, TableDefinition tableBuilder)
    {
        var iface = configType.GetInterfaces().FirstOrDefault(i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition() == typeof(IEntityConfiguration<>) &&
            i.GetGenericArguments()[0] == entityType) ?? throw new InvalidOperationException(
            $"Configuration type '{configType.Name}' does not implement IEntityConfiguration<{entityType.Name}>.");

        var configInstance = Activator.CreateInstance(configType);
        var entityBuilderInstance = CreateEntityBuilder(entityType, tableBuilder);

        var configureMethod = iface.GetMethod(nameof(IEntityConfiguration<>.Configure));
        if (configInstance is not null && entityBuilderInstance is not null && configureMethod is not null)
        {
            configureMethod.Invoke(configInstance, [entityBuilderInstance]);
        }
    }

    private static bool IsScalarType(Type type)
    {
        var unwrapped = Nullable.GetUnderlyingType(type) ?? type;
        return unwrapped.IsPrimitive
               || unwrapped.IsEnum
               || unwrapped == typeof(string)
               || unwrapped == typeof(decimal)
               || unwrapped == typeof(DateTime)
               || unwrapped == typeof(DateTimeOffset)
               || unwrapped == typeof(DateOnly)
               || unwrapped == typeof(TimeOnly)
               || unwrapped == typeof(TimeSpan)
               || unwrapped == typeof(Guid)
               || unwrapped == typeof(byte[]);
    }
}
