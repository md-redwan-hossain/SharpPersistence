namespace SharpPersistence.Migrator.Configuration;

internal sealed class SqlExpressionBuilderOptions
{
    public bool DelimitIdentifiers { get; internal set; } = true;
    public string NestedColumnSeparator { get; internal set; } = "_";
}

public sealed class SqlExpressionBuilderOptionsBuilder
{
    private readonly SqlExpressionBuilderOptions _options;

    internal SqlExpressionBuilderOptionsBuilder(SqlExpressionBuilderOptions options)
    {
        _options = options;
    }

    public SqlExpressionBuilderOptionsBuilder UseDelimitedIdentifiers(bool delimit = true)
    {
        _options.DelimitIdentifiers = delimit;
        return this;
    }

    public SqlExpressionBuilderOptionsBuilder UseNestedColumnSeparator(string separator)
    {
        ArgumentNullException.ThrowIfNull(separator);
        _options.NestedColumnSeparator = separator;
        return this;
    }
}
