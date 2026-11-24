using System.Globalization;
using SharpPersistence.Migrator.Enums;

namespace SharpPersistence.Migrator.Builders;

public class ColumnBuilder
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
        Definition.ComputedColumnSqlRenderer = null;
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

    public ColumnBuilder SetAnnotation<TValue>(string name, TValue? value)
    {
        Definition.SetAnnotation(name, value);
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

public sealed class ColumnBuilder<TEntity> : ColumnBuilder where TEntity : class
{
    internal ColumnBuilder(ColumnDefinition definition) : base(definition)
    {
    }

    public new ColumnBuilder<TEntity> SetAnnotation<TValue>(string name, TValue? value)
    {
        Definition.SetAnnotation(name, value);
        return this;
    }

    public ColumnBuilder<TEntity> HasComputedColumnSql(
        Func<ISqlExpressionBuilder<TEntity>, SqlBuiltExpression> expression,
        bool stored = false)
    {
        ArgumentNullException.ThrowIfNull(expression);

        Definition.ComputedColumnSql = null;
        Definition.ComputedColumnSqlRenderer = (dialect, identifierCase, delimitIdentifiers, nestedSeparator) =>
        {
            ISqlExpressionBuilder<TEntity> builder = new SqlExpressionBuilder<TEntity>
            {
                Dialect = dialect,
                IdentifierCase = identifierCase,
                DelimitIdentifiers = delimitIdentifiers,
                NestedSeparator = nestedSeparator
            };

            var result = expression(builder)
                ?? throw new InvalidOperationException(
                    $"Computed column expression for '{Definition.PropertyName}' on entity '{typeof(TEntity).Name}' returned null.");
            return result.ToString();
        };
        Definition.IsComputedStored = stored;
        return this;
    }
}
