namespace SharpPersistence.Migrator.Enums;

/// <summary>
/// Controls whether a <c>ConfigureEntities</c> callback runs before or after per-entity <c>IEntityConfiguration&lt;T&gt;</c> classes.
/// </summary>
public enum ModelCreationPhase
{
    /// <summary>
    /// Runs before per-entity <c>IEntityConfiguration&lt;T&gt;</c> classes so entity-specific rules can override defaults.
    /// </summary>
    BeforeEntityConfigClass,

    /// <summary>
    /// Runs after per-entity <c>IEntityConfiguration&lt;T&gt;</c> classes so callbacks can inspect annotations and metadata set per entity.
    /// </summary>
    AfterEntityConfigClass
}
