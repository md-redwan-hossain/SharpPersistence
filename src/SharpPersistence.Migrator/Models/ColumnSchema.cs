using System.Text.Json.Serialization;
using SharpPersistence.Migrator.Enums;

namespace SharpPersistence.Migrator.Models;

internal sealed class ColumnSchema
{
    public string PropertyName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;

    [JsonIgnore]
    public string? PreviousColumnName { get; set; }

    [JsonIgnore]
    public string ClrTypeName { get; set; } = string.Empty;
    public string SqlType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxLength { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Precision { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Scale { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsUnicode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultValueSql { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsIdentity { get; set; }

    [JsonIgnore]
    public ValueGenerationMode ValueGenerated { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ComputedColumnSql { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsComputedStored { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Comment { get; set; }

    [JsonIgnore]
    public bool IsConcurrencyToken { get; set; }

    [JsonIgnore]
    public bool IsRowVersion { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsSystemColumn { get; set; }

    [JsonIgnore]
    public Type? ProviderClrType { get; set; }

    [JsonIgnore]
    public object? ValueConverter { get; set; }

    [JsonIgnore]
    public bool IsOwnedColumn { get; set; }

    [JsonIgnore]
    public Dictionary<string, object?> Annotations { get; set; } = new(StringComparer.Ordinal);
}
