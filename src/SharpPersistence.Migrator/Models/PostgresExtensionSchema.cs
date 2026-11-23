namespace SharpPersistence.Migrator.Models;

public sealed record PostgresExtensionSchema(string Name, string? Schema = null);
