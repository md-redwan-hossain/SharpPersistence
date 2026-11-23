using System.Linq.Expressions;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Builders;

public sealed class ForeignKeyBuilder<TDependent, TPrincipal>
    where TDependent : class
    where TPrincipal : class
{
    private readonly TableDefinition _dependentTable;
    private readonly IReadOnlyList<string> _dependentProps;
    private IReadOnlyList<string> _principalProps = [];
    private string? _constraintName;
    private DbReferentialAction _onDelete = DbReferentialAction.NoAction;
    private bool _isCircular;
    private bool _isUnique;

    internal ForeignKeyBuilder(TableDefinition dependentTable, IReadOnlyList<string> dependentProps)
    {
        _dependentTable = dependentTable;
        _dependentProps = dependentProps;
    }

    public ForeignKeyBuilder<TDependent, TPrincipal> References(
        Expression<Func<TPrincipal, object?>> principalKeyExpression)
    {
        _principalProps = ExpressionHelper.ExtractPropertyNames(principalKeyExpression);
        Commit();
        return this;
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

    public ForeignKeyBuilder<TDependent, TPrincipal> IsUnique(bool isUnique = true)
    {
        _isUnique = isUnique;
        Commit();
        return this;
    }

    private void Commit()
    {
        if (_principalProps.Count == 0)
        {
            return;
        }

        _dependentTable.SetForeignKey(
            typeof(TPrincipal),
            _dependentProps,
            _principalProps,
            _constraintName,
            _onDelete,
            _isCircular,
            _isUnique);
    }
}
