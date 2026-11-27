using SharpPersistence.Migrator.Enums;

namespace SharpPersistence.Migrator.Configuration;

internal sealed class OracleMigratorOptions
{
    public OracleDbCompatibility Compatibility { get; internal set; } = OracleDbCompatibility.Oracle19;
}

public sealed class OracleOptionsBuilder : DbMigratorOptionsBuilder
{
    internal OracleOptionsBuilder(BuilderState state) : base(state)
    {
    }

    public OracleOptionsBuilder UseSqlCompatibility(OracleDbCompatibility compatibility)
    {
        State.Options.Oracle.Compatibility = compatibility;
        return this;
    }
}
