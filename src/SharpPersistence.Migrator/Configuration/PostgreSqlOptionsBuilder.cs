using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Configuration;

internal sealed class PostgresMigratorOptions
{
    public IReadOnlyList<PostgresExtensionSchema> Extensions { get; internal set; } = [];
    public IReadOnlyList<PostgresEnumSchema> Enums { get; internal set; } = [];
}

public sealed class PostgreSqlOptionsBuilder : DbMigratorOptionsBuilder
{
    internal PostgreSqlOptionsBuilder(BuilderState state) : base(state)
    {
    }

    public PostgreSqlOptionsBuilder HasPostgresExtension(string name, string? schema = null)
    {
        State.Extensions.RemoveAll(e => string.Equals(e.Name, name, StringComparison.Ordinal));
        State.Extensions.Add(new PostgresExtensionSchema(name, schema));
        return this;
    }

    public PostgreSqlOptionsBuilder HasPostgresEnum<TEnum>(string? name = null, string? schema = null)
        where TEnum : struct, Enum
    {
        State.EnumRegistrations.RemoveAll(e => e.EnumType == typeof(TEnum));
        State.EnumRegistrations.Add((typeof(TEnum), name, schema));
        return this;
    }
}
