using SharpPersistence.Migrator.Configuration;

namespace SharpPersistence.Migrator.Abstractions;

public interface IDesignTimeDbMigratorFactory
{
    void Configure(DbMigratorOptionsBuilder options, string[] args);
}
