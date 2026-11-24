using SharpPersistence.Migrator.Diffing;

namespace SharpPersistence.Migrator.Abstractions;

internal interface ISqlDialectGenerator
{
    string GenerateSql(IReadOnlyList<SchemaOperation> operations);
}
