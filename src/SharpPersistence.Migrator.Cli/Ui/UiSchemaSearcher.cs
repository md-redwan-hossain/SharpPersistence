using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Cli.Ui;

internal static class UiSchemaSearcher
{
    public static UiSearchResponseDto Search(
        IReadOnlyList<(string RegistryName, DatabaseModel Model)> scannedRegistries,
        string? query,
        int limit = 40)
    {
        var q = (query ?? string.Empty).Trim();
        var clampedLimit = Math.Clamp(limit, 1, 100);
        if (q.Length < 2)
        {
            return new UiSearchResponseDto(q, []);
        }

        var hits = new List<UiSearchHitDto>(Math.Min(clampedLimit, 40));

        foreach (var (registryName, model) in scannedRegistries)
        {
            foreach (var t in model.Tables)
            {
                var shortEntity = UiDataService.GetShortName(t.EntityName);

                foreach (var c in t.Columns)
                {
                    if (hits.Count >= clampedLimit)
                    {
                        return new UiSearchResponseDto(q, hits);
                    }

                    if (c.ColumnName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        c.PropertyName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        c.SqlType.Contains(q, StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add(new UiSearchHitDto(
                            Kind: "Column",
                            Name: c.ColumnName,
                            Detail: $"{c.PropertyName} ({c.SqlType})",
                            RegistryName: registryName,
                            EntityName: t.EntityName,
                            ShortEntityName: shortEntity,
                            QualifiedName: t.QualifiedName));
                    }
                }

                foreach (var ix in t.Indexes)
                {
                    if (hits.Count >= clampedLimit)
                    {
                        return new UiSearchResponseDto(q, hits);
                    }

                    if (ix.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        ix.ColumnNames.Any(col => col.Contains(q, StringComparison.OrdinalIgnoreCase)))
                    {
                        hits.Add(new UiSearchHitDto(
                            Kind: "Index",
                            Name: ix.Name,
                            Detail: $"{(ix.IsUnique ? "UNIQUE " : string.Empty)}({string.Join(", ", ix.ColumnNames)})",
                            RegistryName: registryName,
                            EntityName: t.EntityName,
                            ShortEntityName: shortEntity,
                            QualifiedName: t.QualifiedName));
                    }
                }

                foreach (var fk in t.ForeignKeys)
                {
                    if (hits.Count >= clampedLimit)
                    {
                        return new UiSearchResponseDto(q, hits);
                    }

                    if (fk.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        fk.PrincipalTable.Contains(q, StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add(new UiSearchHitDto(
                            Kind: "ForeignKey",
                            Name: fk.Name,
                            Detail: $"({string.Join(", ", fk.DependentColumns)}) -> {fk.PrincipalTable}({string.Join(", ", fk.PrincipalColumns)})",
                            RegistryName: registryName,
                            EntityName: t.EntityName,
                            ShortEntityName: shortEntity,
                            QualifiedName: t.QualifiedName));
                    }
                }

                foreach (var ck in t.CheckConstraints)
                {
                    if (hits.Count >= clampedLimit)
                    {
                        return new UiSearchResponseDto(q, hits);
                    }

                    if (ck.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        ck.Sql.Contains(q, StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add(new UiSearchHitDto(
                            Kind: "CheckConstraint",
                            Name: ck.Name,
                            Detail: ck.Sql,
                            RegistryName: registryName,
                            EntityName: t.EntityName,
                            ShortEntityName: shortEntity,
                            QualifiedName: t.QualifiedName));
                    }
                }

                if (t.ExclusionConstraints is { Count: > 0 })
                {
                    foreach (var ex in t.ExclusionConstraints)
                    {
                        if (hits.Count >= clampedLimit)
                        {
                            return new UiSearchResponseDto(q, hits);
                        }

                        if (ex.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            ex.Method.Contains(q, StringComparison.OrdinalIgnoreCase))
                        {
                            hits.Add(new UiSearchHitDto(
                                Kind: "ExclusionConstraint",
                                Name: ex.Name,
                                Detail: $"EXCLUDE USING {ex.Method}",
                                RegistryName: registryName,
                                EntityName: t.EntityName,
                                ShortEntityName: shortEntity,
                                QualifiedName: t.QualifiedName));
                        }
                    }
                }
            }
        }

        return new UiSearchResponseDto(q, hits);
    }
}
