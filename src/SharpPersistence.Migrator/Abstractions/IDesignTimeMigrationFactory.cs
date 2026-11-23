using SharpPersistence.Migrator.Configuration;

namespace SharpPersistence.Migrator.Abstractions;

public interface IDesignTimeMigrationFactory
{
    void Configure(MigrationManagerOptionsBuilder options, string[] args);
}
