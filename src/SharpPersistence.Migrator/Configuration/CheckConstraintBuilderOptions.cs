namespace SharpPersistence.Migrator.Configuration;

public sealed class CheckConstraintBuilderOptions
{
    public bool DelimitIdentifiers { get; internal set; } = true;
    public string NestedColumnSeparator { get; internal set; } = "_";
}

public sealed class CheckConstraintBuilderOptionsBuilder
{
    private readonly CheckConstraintBuilderOptions _options;

    internal CheckConstraintBuilderOptionsBuilder(CheckConstraintBuilderOptions options)
    {
        _options = options;
    }

    public CheckConstraintBuilderOptionsBuilder UseDelimitedIdentifiers(bool delimit = true)
    {
        _options.DelimitIdentifiers = delimit;
        return this;
    }

    public CheckConstraintBuilderOptionsBuilder UseNestedColumnSeparator(string separator)
    {
        ArgumentNullException.ThrowIfNull(separator);
        _options.NestedColumnSeparator = separator;
        return this;
    }
}
