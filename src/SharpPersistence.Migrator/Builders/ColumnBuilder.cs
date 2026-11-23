using System.Globalization;
using SharpPersistence.Migrator.Enums;

namespace SharpPersistence.Migrator.Builders;

public sealed class ColumnBuilder
{
    internal ColumnDefinition Definition { get; }

    internal ColumnBuilder(ColumnDefinition definition)
    {
        Definition = definition;
    }

    public ColumnBuilder HasColumnName(string columnName)
    {
        Definition.ColumnName = columnName;
        return this;
    }

    public ColumnBuilder HasPreviousColumnName(string previousColumnName)
    {
        Definition.PreviousColumnName = previousColumnName;
        return this;
    }

    public ColumnBuilder HasColumnType(string sqlType)
    {
        Definition.ExplicitSqlType = sqlType;
        return this;
    }

    public ColumnBuilder HasMaxLength(int maxLength)
    {
        Definition.MaxLength = maxLength;
        return this;
    }

    public ColumnBuilder HasPrecision(int precision, int scale)
    {
        Definition.Precision = precision;
        Definition.Scale = scale;
        return this;
    }

    public ColumnBuilder IsRequired(bool required = true)
    {
        Definition.IsNullable = !required;
        return this;
    }

    public ColumnBuilder IsUnicode(bool unicode = true)
    {
        Definition.IsUnicode = unicode;
        return this;
    }

    public ColumnBuilder HasDefaultValueSql(string? defaultSql)
    {
        Definition.DefaultValueSql = defaultSql;
        return this;
    }

    public ColumnBuilder HasDefaultValue(object? value)
    {
        Definition.DefaultValueSql = FormatDefaultLiteral(value);
        return this;
    }

    public ColumnBuilder HasComputedColumnSql(string? sql, bool stored = false)
    {
        Definition.ComputedColumnSql = sql;
        Definition.IsComputedStored = stored;
        return this;
    }

    public ColumnBuilder HasComment(string? comment)
    {
        Definition.Comment = comment;
        return this;
    }

    public ColumnBuilder HasConversion<TProvider>() =>
        HasConversion(typeof(TProvider));

    public ColumnBuilder HasConversion(Type providerClrType)
    {
        Definition.ProviderClrType = providerClrType;
        return this;
    }

    public ColumnBuilder ValueGeneratedOnAdd(bool isIdentity = true)
    {
        Definition.IsIdentity = isIdentity;
        Definition.ValueGenerated = isIdentity ? ValueGenerationMode.OnAdd : ValueGenerationMode.Default;
        return this;
    }

    public ColumnBuilder ValueGeneratedNever()
    {
        Definition.IsIdentity = false;
        Definition.ValueGenerated = ValueGenerationMode.Never;
        return this;
    }

    public ColumnBuilder ValueGeneratedOnAddOrUpdate()
    {
        Definition.IsIdentity = false;
        Definition.ValueGenerated = ValueGenerationMode.OnAddOrUpdate;
        return this;
    }

    public ColumnBuilder IsConcurrencyToken(bool concurrencyToken = true)
    {
        Definition.IsConcurrencyToken = concurrencyToken;
        return this;
    }

    public ColumnBuilder IsRowVersion()
    {
        Definition.IsRowVersion = true;
        Definition.IsConcurrencyToken = true;
        Definition.ValueGenerated = ValueGenerationMode.OnAddOrUpdate;
        return this;
    }

    private static string FormatDefaultLiteral(object? value) => value switch
    {
        null => "NULL",
        bool b => b ? "TRUE" : "FALSE",
        string s => $"'{s.Replace("'", "''")}'",
        char c => $"'{c.ToString().Replace("'", "''")}'",
        Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        Guid g => $"'{g}'",
        DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss.ffffff}'",
        DateTimeOffset dto => $"'{dto:yyyy-MM-dd HH:mm:ss.ffffffzzz}'",
        DateOnly d => $"'{d:yyyy-MM-dd}'",
        TimeOnly t => $"'{t:HH:mm:ss.ffffff}'",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => $"'{value.ToString()?.Replace("'", "''")}'"
    };
}
