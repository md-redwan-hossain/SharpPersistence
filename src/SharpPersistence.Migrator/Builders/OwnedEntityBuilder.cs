using System.Linq.Expressions;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Builders;

public sealed class OwnedEntityBuilder<TOwner, TOwned>
    where TOwner : class
    where TOwned : class
{
    private readonly TableDefinition _table;
    private readonly OwnedNavigationDefinition _navigation;

    internal OwnedEntityBuilder(TableDefinition table, OwnedNavigationDefinition navigation)
    {
        _table = table;
        _navigation = navigation;
    }

    public ColumnBuilder<TOwner> Property<TProperty>(Expression<Func<TOwned, TProperty>> propertyExpression)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);
        var propName = ExpressionHelper.ExtractSinglePropertyName(propertyExpression);
        var col = _table.GetOrAddOwnedColumn(_navigation, propName, typeof(TProperty));
        return new ColumnBuilder<TOwner>(col);
    }

    public OwnedEntityBuilder<TOwner, TOwned> Ignore(Expression<Func<TOwned, object?>> propertyExpression)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);
        var propNames = ExpressionHelper.ExtractPropertyNames(propertyExpression);
        foreach (var propName in propNames)
        {
            _table.IgnoreOwnedProperty(_navigation, propName);
        }

        return this;
    }

    public IndexBuilder<TOwner, TKey> HasIndex<TKey>(Expression<Func<TOwned, TKey>> indexExpression)
    {
        ArgumentNullException.ThrowIfNull(indexExpression);
        var propNames = ExpressionHelper.ExtractPropertyNames(indexExpression)
            .Select(p => $"{_navigation.PropertyPathPrefix}.{p}")
            .ToList();
        return new IndexBuilder<TOwner, TKey>(_table.AddIndex(propNames));
    }

    public OwnedEntityBuilder<TOwner, TOwned> OwnsOne<TNested>(
        Expression<Func<TOwned, TNested?>> navigationExpression,
        Action<OwnedEntityBuilder<TOwner, TNested>>? configure = null)
        where TNested : class
    {
        ArgumentNullException.ThrowIfNull(navigationExpression);
        var navPropName = ExpressionHelper.ExtractSinglePropertyName(navigationExpression);
        var nestedNav = _table.GetOrAddOwnedNavigation(
            parentType: typeof(TOwned),
            navigationPropertyName: navPropName,
            ownedClrType: typeof(TNested),
            parentNavigation: _navigation);

        configure?.Invoke(new OwnedEntityBuilder<TOwner, TNested>(_table, nestedNav));
        return this;
    }
}
