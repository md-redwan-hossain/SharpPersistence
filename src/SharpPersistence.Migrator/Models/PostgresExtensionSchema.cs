namespace SharpPersistence.Migrator.Models;

internal sealed record PostgresExtensionSchema(string Name, string? Schema = null);
