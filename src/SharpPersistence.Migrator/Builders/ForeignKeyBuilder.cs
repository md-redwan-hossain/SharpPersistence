using System.Linq.Expressions;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Builders;

public sealed class ForeignKeyReferenceBuilder<TDependent>
    where TDependent : class
{
    private readonly TableDefinition _dependentTable;
    private readonly IReadOnlyList<string> _dependentProps;

    internal ForeignKeyReferenceBuilder(TableDefinition dependentTable, IReadOnlyList<string> dependentProps)
    {
        _dependentTable = dependentTable;
        _dependentProps = dependentProps;
    }

    public ForeignKeyBuilder<TDependent, TPrincipal> References<TPrincipal>(
        Expression<Func<TPrincipal, object?>> principalKeyExpression)
        where TPrincipal : class
    {
        var principalProps = ExpressionHelper.ExtractPropertyNames(principalKeyExpression);
        return new ForeignKeyBuilder<TDependent, TPrincipal>(_dependentTable, _dependentProps, principalProps);
    }
}

public sealed class ForeignKeyBuilder<TDependent, TPrincipal>
    where TDependent : class
    where TPrincipal : class
{
    private readonly TableDefinition _dependentTable;
    private readonly IReadOnlyList<string> _dependentProps;
    private readonly IReadOnlyList<string> _principalProps;
    private string? _constraintName;
    private DbReferentialAction _onDelete = DbReferentialAction.NoAction;
    private bool _isCircular;
    private bool _isOneToOne;

    internal ForeignKeyBuilder(
        TableDefinition dependentTable,
        IReadOnlyList<string> dependentProps,
        IReadOnlyList<string> principalProps)
    {
        _dependentTable = dependentTable;
        _dependentProps = dependentProps;
        _principalProps = principalProps;
        Commit();
    }

    public ForeignKeyBuilder<TDependent, TPrincipal> HasConstraintName(string name)
    {
        _constraintName = name;
        Commit();
        return this;
    }

    public ForeignKeyBuilder<TDependent, TPrincipal> OnDelete(DbReferentialAction action)
    {
        _onDelete = action;
        Commit();
        return this;
    }

    public ForeignKeyBuilder<TDependent, TPrincipal> IsCircular(bool isCircular = true)
    {
        _isCircular = isCircular;
        Commit();
        return this;
    }

    public ForeignKeyBuilder<TDependent, TPrincipal> IsOneToOne(bool isOneToOne = true)
    {
        _isOneToOne = isOneToOne;
        Commit();
        return this;
    }

    private void Commit()
    {
        _dependentTable.SetForeignKey(
            typeof(TPrincipal),
            _dependentProps,
            _principalProps,
            _constraintName,
            _onDelete,
            _isCircular,
            _isOneToOne);
    }
}
