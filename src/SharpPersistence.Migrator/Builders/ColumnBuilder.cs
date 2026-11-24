using System.Globalization;
using SharpPersistence.Migrator.Enums;

namespace SharpPersistence.Migrator.Builders;

public abstract class ColumnBuilderBase<TBuilder>
    where TBuilder : ColumnBuilderBase<TBuilder>
{
    internal ColumnDefinition Definition { get; }

    internal ColumnBuilderBase(ColumnDefinition definition)
    {
        Definition = definition;
    }

    private TBuilder Self => (TBuilder)this;

    public TBuilder HasColumnName(string columnName)
    {
        Definition.ColumnName = columnName;
        return Self;
    }

    public TBuilder HasPreviousColumnName(string previousColumnName)
    {
        Definition.PreviousColumnName = previousColumnName;
        return Self;
    }

    public TBuilder HasColumnType(string sqlType)
    {
        Definition.ExplicitSqlType = sqlType;
        return Self;
    }

    public TBuilder HasMaxLength(int maxLength)
    {
        Definition.MaxLength = maxLength;
        return Self;
    }

    public TBuilder HasPrecision(int precision, int scale)
    {
        Definition.Precision = precision;
        Definition.Scale = scale;
        return Self;
    }

    public TBuilder IsRequired(bool required = true)
    {
        Definition.IsNullable = !required;
        return Self;
    }

    public TBuilder IsUnicode(bool unicode = true)
    {
        Definition.IsUnicode = unicode;
        return Self;
    }

    public TBuilder HasDefaultValueSql(string? defaultSql)
    {
        Definition.DefaultValueSql = defaultSql;
        return Self;
    }

    public TBuilder HasDefaultValue<TValue>(TValue? value)
    {
        Definition.DefaultValueSql = FormatDefaultLiteral(value);
        return Self;
    }

    public TBuilder HasComputedColumnSql(string? sql, bool stored = false)
    {
        Definition.ComputedColumnSql = sql;
        Definition.ComputedColumnSqlRenderer = null;
        Definition.IsComputedStored = stored;
        return Self;
    }

    public TBuilder HasComment(string? comment)
    {
        Definition.Comment = comment;
        return Self;
    }

    public TBuilder HasConversion<TProvider>() =>
        HasConversion(typeof(TProvider));

    public TBuilder HasConversion(Type providerClrType)
    {
        Definition.ProviderClrType = providerClrType;
        return Self;
    }

    public TBuilder ValueGeneratedOnAdd(bool isIdentity = true)
    {
        Definition.IsIdentity = isIdentity;
        Definition.ValueGenerated = isIdentity ? ValueGenerationMode.OnAdd : ValueGenerationMode.Default;
        return Self;
    }

    public TBuilder ValueGeneratedNever()
    {
        Definition.IsIdentity = false;
        Definition.ValueGenerated = ValueGenerationMode.Never;
        return Self;
    }

    public TBuilder ValueGeneratedOnAddOrUpdate()
    {
        Definition.IsIdentity = false;
        Definition.ValueGenerated = ValueGenerationMode.OnAddOrUpdate;
        return Self;
    }

    public TBuilder IsConcurrencyToken(bool concurrencyToken = true)
    {
        Definition.IsConcurrencyToken = concurrencyToken;
        return Self;
    }

    public TBuilder IsRowVersion()
    {
        Definition.IsRowVersion = true;
        Definition.IsConcurrencyToken = true;
        Definition.ValueGenerated = ValueGenerationMode.OnAddOrUpdate;
        return Self;
    }

    public TBuilder SetAnnotation<TValue>(string name, TValue? value)
    {
        Definition.SetAnnotation(name, value);
        return Self;
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

public sealed class ColumnBuilder : ColumnBuilderBase<ColumnBuilder>
{
    internal ColumnBuilder(ColumnDefinition definition) : base(definition)
    {
    }
}

public sealed class ColumnBuilder<TEntity> : ColumnBuilderBase<ColumnBuilder<TEntity>> where TEntity : class
{
    internal ColumnBuilder(ColumnDefinition definition) : base(definition)
    {
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
