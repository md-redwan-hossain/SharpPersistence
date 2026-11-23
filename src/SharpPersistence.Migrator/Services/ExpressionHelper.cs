using System.Linq.Expressions;
using System.Reflection;

namespace SharpPersistence.Migrator.Services;

internal static class ExpressionHelper
{
    public static string ExtractSinglePropertyName<T, TProp>(Expression<Func<T, TProp>> expr)
    {
        var names = ExtractFromBody(expr.Body);
        if (names.Count != 1)
        {
            throw new InvalidOperationException($"Expected a single property expression, got: {expr}");
        }

        return names[0];
    }

    public static IReadOnlyList<string> ExtractPropertyNames<T, TKey>(Expression<Func<T, TKey>> expr)
    {
        return ExtractFromBody(expr.Body);
    }

    private static IReadOnlyList<string> ExtractFromBody(Expression body)
    {
        if (body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            body = unary.Operand;
        }

        if (body is MemberExpression member && member.Member is PropertyInfo prop)
        {
            return [prop.Name];
        }

        if (body is NewExpression newExpr && newExpr.Arguments.Count > 0)
        {
            var props = new List<string>(newExpr.Arguments.Count);
            foreach (var arg in newExpr.Arguments)
            {
                var unwrapped = arg is UnaryExpression u && u.NodeType == ExpressionType.Convert ? u.Operand : arg;
                if (unwrapped is not MemberExpression m || m.Member is not PropertyInfo p)
                {
                    throw new InvalidOperationException($"Unsupported property expression argument: {arg}");
                }

                props.Add(p.Name);
            }

            return props;
        }

        throw new InvalidOperationException($"Unsupported property expression: {body}");
    }
}
