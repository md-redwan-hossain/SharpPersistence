using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.EfCore;

public static class EfCoreModelBridge
{
    public static void ApplySchemaToEfCore(
        this ModelBuilder modelBuilder,
        DatabaseModel schema,
        IEnumerable<Type>? includedEntityTypes = null)
    {
        HashSet<Type>? includedSet = includedEntityTypes is null ? null : [.. includedEntityTypes];
        if (schema.Sequences is { Count: > 0 })
        {
            foreach (var seq in schema.Sequences)
            {
                var seqClrType = seq.ClrTypeName switch
                {
                    "short" => typeof(short),
                    "int" => typeof(int),
                    _ => typeof(long)
                };

                var seqBuilder = modelBuilder.HasSequence(seqClrType, seq.Name, seq.Schema)
                    .StartsAt(seq.StartValue)
                    .IncrementsBy(seq.IncrementBy);

                if (seq.MinValue.HasValue)
                {
                    seqBuilder.HasMin(seq.MinValue.Value);
                }

                if (seq.MaxValue.HasValue)
                {
                    seqBuilder.HasMax(seq.MaxValue.Value);
                }

                seqBuilder.IsCyclic(seq.IsCyclic);
            }
        }

        var clrTypeByEntityName = schema.ByClrType.ToDictionary(
            kvp => kvp.Value.EntityName,
            kvp => kvp.Key,
            StringComparer.Ordinal);

        foreach (var (clrType, table) in schema.ByClrType)
        {
            if (includedSet is not null && !includedSet.Contains(clrType))
            {
                continue;
            }

            modelBuilder.Entity(clrType, entity =>
            {
                entity.ToTable(table.TableName, table.Schema, tb =>
                {
                    if (table.ExcludeFromMigrations)
                    {
                        tb.ExcludeFromMigrations();
                    }

                    if (!string.IsNullOrWhiteSpace(table.Comment))
                    {
                        tb.HasComment(table.Comment);
                    }

                    foreach (var ck in table.CheckConstraints)
                    {
                        tb.HasCheckConstraint(ck.Name, ck.Sql);
                    }
                });

                foreach (var ignoredProp in table.IgnoredProperties)
                {
                    entity.Ignore(ignoredProp);
                }

                if (table.QueryFilter is not null)
                {
                    entity.HasQueryFilter(table.QueryFilter);
                }

                foreach (var (name, value) in table.Annotations)
                {
                    entity.Metadata.SetAnnotation(name, value);
                }

                foreach (var (filterKey, filter) in table.NamedQueryFilters)
                {
                    entity.HasQueryFilter(filterKey, filter);
                }

                if (table.PrimaryKey is not null && table.PrimaryKey.PropertyNames.Count > 0)
                {
                    entity.HasKey([.. table.PrimaryKey.PropertyNames])
                          .HasName(table.PrimaryKey.Name);
                }

                foreach (var col in table.Columns)
                {
                    if (col.IsOwnedColumn)
                    {
                        continue;
                    }

                    ApplyColumnFacets(entity.Property(col.PropertyName), col);
                }

                if (table.OwnedNavigations is { Count: > 0 })
                {
                    foreach (var owned in table.OwnedNavigations)
                    {
                        entity.OwnsOne(owned.OwnedClrType, owned.NavigationName, ob =>
                            ApplyOwnedNavigation(ob, owned, table, owned.IsOptional));

                        if (!owned.IsOptional)
                        {
                            entity.Navigation(owned.NavigationName).IsRequired();
                        }
                    }
                }

                foreach (var ix in table.Indexes)
                {
                    if (ix.PropertyNames.Any(p => p.Contains('.', StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    ApplyIndexFacets(entity.HasIndex([.. ix.PropertyNames]), ix);
                }

                foreach (var fk in table.ForeignKeys)
                {
                    if (!string.IsNullOrWhiteSpace(fk.PrincipalEntityName) &&
                        clrTypeByEntityName.TryGetValue(fk.PrincipalEntityName, out var principalClrType) &&
                        (includedSet is null || includedSet.Contains(principalClrType)))
                    {
                        var isOneToOne =
                            (table.PrimaryKey is not null &&
                             table.PrimaryKey.PropertyNames.SequenceEqual(fk.DependentProperties, StringComparer.Ordinal))
                            || table.Indexes.Any(ix =>
                                ix.IsUnique &&
                                string.IsNullOrWhiteSpace(ix.FilterSql) &&
                                ix.PropertyNames.SequenceEqual(fk.DependentProperties, StringComparer.Ordinal));

                        if (isOneToOne)
                        {
                            var rel = entity.HasOne(principalClrType)
                                .WithOne()
                                .HasForeignKey(clrType, [.. fk.DependentProperties])
                                .HasConstraintName(fk.Name)
                                .OnDelete(MapDeleteBehavior(fk.OnDelete));

                            if (fk.PrincipalProperties.Count > 0)
                            {
                                rel.HasPrincipalKey(principalClrType, [.. fk.PrincipalProperties]);
                            }
                        }
                        else
                        {
                            var rel = entity.HasOne(principalClrType)
                                .WithMany()
                                .HasForeignKey([.. fk.DependentProperties])
                                .HasConstraintName(fk.Name)
                                .OnDelete(MapDeleteBehavior(fk.OnDelete));

                            if (fk.PrincipalProperties.Count > 0)
                            {
                                rel.HasPrincipalKey([.. fk.PrincipalProperties]);
                            }
                        }
                    }
                }
            });
        }
    }

    private static void ApplyOwnedNavigation(
        OwnedNavigationBuilder ownedBuilder,
        OwnedNavigationSchema owned,
        TableSchema table,
        bool insideOptionalOwned)
    {
        foreach (var ignoredProp in owned.IgnoredProperties)
        {
            ownedBuilder.Ignore(ignoredProp);
        }

        var prefix = $"{owned.PropertyPathPrefix}.";
        var mappedLocalProps = new HashSet<string>(StringComparer.Ordinal);
        foreach (var col in table.Columns)
        {
            if (!col.IsOwnedColumn || !col.PropertyName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var localPropName = col.PropertyName[prefix.Length..];
            if (localPropName.Contains('.', StringComparison.Ordinal))
            {
                continue;
            }

            mappedLocalProps.Add(localPropName);
            ApplyColumnFacets(ownedBuilder.Property(localPropName), col, insideOptionalOwned);
        }

        var nestedNavNames = new HashSet<string>(
            owned.NestedNavigations.Select(n => n.NavigationName),
            StringComparer.Ordinal);

        // Ignore any unconfigured CLR properties on the owned type (e.g. an unconfigured nested owned type
        // that is configured on another sibling OwnsOne of the same CLR type) so EF Core does not throw.
        foreach (var propInfo in owned.OwnedClrType.GetProperties(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!mappedLocalProps.Contains(propInfo.Name) &&
                !nestedNavNames.Contains(propInfo.Name) &&
                !owned.IgnoredProperties.Contains(propInfo.Name))
            {
                ownedBuilder.Ignore(propInfo.Name);
            }
        }

        foreach (var ix in table.Indexes)
        {
            if (ix.PropertyNames.Count == 0 ||
                !ix.PropertyNames.All(p =>
                    p.StartsWith(prefix, StringComparison.Ordinal) &&
                    !p[prefix.Length..].Contains('.', StringComparison.Ordinal)))
            {
                continue;
            }

            var localProps = ix.PropertyNames.Select(p => p[prefix.Length..]).ToArray();
            ApplyIndexFacets(ownedBuilder.HasIndex(localProps), ix);
        }

        foreach (var nested in owned.NestedNavigations)
        {
            var nestedOptional = insideOptionalOwned || nested.IsOptional;
            ownedBuilder.OwnsOne(nested.OwnedClrType, nested.NavigationName, nb =>
                ApplyOwnedNavigation(nb, nested, table, nestedOptional));

            if (!nested.IsOptional)
            {
                ownedBuilder.Navigation(nested.NavigationName).IsRequired();
            }
        }
    }

    private static void ApplyColumnFacets(PropertyBuilder prop, ColumnSchema col, bool insideOptionalOwned = false)
    {
        prop.HasColumnName(col.ColumnName)
            .HasColumnType(col.SqlType);

        if (!insideOptionalOwned)
        {
            var clrType = prop.Metadata.ClrType;
            var isNonNullableValueType = clrType.IsValueType && Nullable.GetUnderlyingType(clrType) is null;
            if (!col.IsNullable || !isNonNullableValueType)
            {
                prop.IsRequired(!col.IsNullable);
            }
        }

        if (col.ValueConverter is ValueConverter valueConverter)
        {
            prop.HasConversion(valueConverter);
        }
        else if (col.ProviderClrType is not null)
        {
            prop.HasConversion(col.ProviderClrType);
        }

        if (col.MaxLength.HasValue)
        {
            prop.HasMaxLength(col.MaxLength.Value);
        }

        if (col.IsUnicode.HasValue)
        {
            prop.IsUnicode(col.IsUnicode.Value);
        }

        if (col.Precision.HasValue)
        {
            if (col.Scale.HasValue)
            {
                prop.HasPrecision(col.Precision.Value, col.Scale.Value);
            }
            else
            {
                prop.HasPrecision(col.Precision.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(col.DefaultValueSql))
        {
            prop.HasDefaultValueSql(col.DefaultValueSql);
        }

        if (!string.IsNullOrWhiteSpace(col.ComputedColumnSql))
        {
            prop.HasComputedColumnSql(col.ComputedColumnSql, col.IsComputedStored);
        }

        if (!string.IsNullOrWhiteSpace(col.Comment))
        {
            prop.HasComment(col.Comment);
        }

        if (col.IsRowVersion)
        {
            prop.IsRowVersion();
        }
        else if (col.IsConcurrencyToken)
        {
            prop.IsConcurrencyToken();
        }

        if (col.ValueGenerated == ValueGenerationMode.Never)
        {
            prop.ValueGeneratedNever();
        }
        else if (col.ValueGenerated == ValueGenerationMode.OnAddOrUpdate)
        {
            prop.ValueGeneratedOnAddOrUpdate();
        }
        else if (col.IsIdentity || col.ValueGenerated == ValueGenerationMode.OnAdd)
        {
            prop.ValueGeneratedOnAdd();
        }

        foreach (var (name, value) in col.Annotations)
        {
            prop.Metadata.SetAnnotation(name, value);
        }
    }

    private static void ApplyIndexFacets(IndexBuilder ixBuilder, IndexSchema ix)
    {
        ixBuilder.HasDatabaseName(ix.Name)
            .IsUnique(ix.IsUnique);

        if (!string.IsNullOrWhiteSpace(ix.FilterSql))
        {
            ixBuilder.HasFilter(ix.FilterSql);
        }

        if (ix.IsDescending is { Count: > 0 })
        {
            ixBuilder.IsDescending([.. ix.IsDescending]);
        }
    }

    private static DeleteBehavior MapDeleteBehavior(DbReferentialAction action) => action switch
    {
        DbReferentialAction.Cascade => DeleteBehavior.Cascade,
        DbReferentialAction.SetNull => DeleteBehavior.SetNull,
        DbReferentialAction.Restrict => DeleteBehavior.Restrict,
        _ => DeleteBehavior.NoAction
    };
}
