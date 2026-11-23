using SharpPersistence.Migrator.Diffing;

namespace SharpPersistence.Migrator.Abstractions;

public interface ISqlDialectGenerator
{
    string GenerateSql(IReadOnlyList<SchemaOperation> operations);
}
