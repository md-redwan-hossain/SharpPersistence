using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.EfCore;

public static class EfCoreModelBridge
{
    public static void ApplySchemaToEfCore(this ModelBuilder modelBuilder, DatabaseSchema schema)
    {
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
            modelBuilder.Entity(clrType, entity =>
            {
                entity.ToTable(table.TableName, table.Schema, tb =>
                {
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
                    var prop = entity.Property(col.PropertyName)
                        .HasColumnName(col.ColumnName)
                        .HasColumnType(col.SqlType)
                        .IsRequired(!col.IsNullable);

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
                        prop.HasPrecision(col.Precision.Value, col.Scale ?? 0);
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
                }

                foreach (var ix in table.Indexes)
                {
                    var ixBuilder = entity.HasIndex([.. ix.PropertyNames])
                        .HasDatabaseName(ix.Name)
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

                foreach (var fk in table.ForeignKeys)
                {
                    if (!string.IsNullOrWhiteSpace(fk.PrincipalEntityName) &&
                        clrTypeByEntityName.TryGetValue(fk.PrincipalEntityName, out var principalClrType))
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

    private static DeleteBehavior MapDeleteBehavior(DbReferentialAction action) => action switch
    {
        DbReferentialAction.Cascade => DeleteBehavior.Cascade,
        DbReferentialAction.SetNull => DeleteBehavior.SetNull,
        DbReferentialAction.Restrict => DeleteBehavior.Restrict,
        _ => DeleteBehavior.NoAction
    };
}
