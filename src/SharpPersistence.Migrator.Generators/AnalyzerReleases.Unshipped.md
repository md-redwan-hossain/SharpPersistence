; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SPM001 | SharpPersistence.Migrator | Error | EntityRegistry class must be sealed, partial, and implement IEntityRegistry
SPM002 | SharpPersistence.Migrator | Error | EntityConfiguration class must be sealed, have an accessible parameterless constructor, and implement IEntityConfiguration<TEntity>
SPM003 | SharpPersistence.Migrator | Error | Multiple EntityConfiguration classes found for the same entity
SPM004 | SharpPersistence.Migrator | Error | EntityRegistry class must declare public void Configure(IEntityRegistryBuilder registry)
SPM005 | SharpPersistence.Migrator | Error | Entity matched by more than one EntityRegistry in the same compilation
SPM006 | SharpPersistence.Migrator | Error | Entity or marker attribute decorated for compile-time discovery must be public
SPM007 | SharpPersistence.Migrator | Warning | EntityConfiguration class is not matched by any EntityRegistry in this compilation
SPM008 | SharpPersistence.Migrator | Error | DbContext class decorated with BindEntityRegistry must be sealed, partial, and inherit DbContext
SPM009 | SharpPersistence.Migrator | Error | EntityRegistryItem with Exclude=true cannot also specify ReadOnly or Pluralize
SPM010 | SharpPersistence.Migrator | Error | Duplicate BindEntityRegistry or EntityRegistryItem attribute on the same DbContext
SPM011 | SharpPersistence.Migrator | Error | EntityRegistryItem requires a bound EntityRegistry containing the target entity
SPM012 | SharpPersistence.Migrator | Error | Generated DbContext property name collides with another entity or existing member
SPM013 | SharpPersistence.Migrator | Error | Custom DbContext constructor or OnModelCreating does not wire DatabaseModel via ApplySchemaToEfCore
SPM014 | SharpPersistence.Migrator | Error | Calling IsRequired() on a property inside an optional OwnsOne navigation is not allowed
SPM015 | SharpPersistence.Migrator | Error | ConfigureEntityStubs cannot map a type to itself or to another ReadOnly entity stub
