using System.Reflection;
using Dapper;
using SharpPersistence.Abstractions;

namespace SharpPersistence.Dapper;

public static class Utils
{
    public static bool IsJsonDeserializableImplementor(Type type)
    {
        return type is { IsInterface: false, IsAbstract: false, IsGenericTypeDefinition: false }
               && typeof(IJsonDeserializable).IsAssignableFrom(type);
    }

    public static bool IsJsonDeserializableImplementor<T>()
    {
        var type = typeof(T);
        return type is { IsInterface: false, IsAbstract: false, IsGenericTypeDefinition: false }
               && typeof(IJsonDeserializable).IsAssignableFrom(type);
    }

    public static void RegisterJsonTypeHandler<T>() where T : IJsonDeserializable
    {
        var handlerType = Activator.CreateInstance(typeof(JsonTypeHandler<>).MakeGenericType(typeof(T)));

        if (handlerType is SqlMapper.ITypeHandler handler)
        {
            SqlMapper.AddTypeHandler(typeof(T), handler);
        }
    }

    public static void RegisterJsonTypeHandler(Type targetType)
    {
        var handlerType = Activator.CreateInstance(typeof(JsonTypeHandler<>).MakeGenericType(targetType));

        if (handlerType is SqlMapper.ITypeHandler handler)
        {
            SqlMapper.AddTypeHandler(targetType, handler);
        }
    }

    public static void RegisterJsonTypeHandlersFromAssembly(Assembly assembly, params Type[] otherTypes)
    {
        var types = assembly
            .GetTypes()
            .Where(IsJsonDeserializableImplementor);

        List<Type> finalTypes = [..types, ..otherTypes];

        foreach (var targetType in finalTypes)
        {
            var instance = Activator.CreateInstance(typeof(JsonTypeHandler<>).MakeGenericType(targetType));

            if (instance is SqlMapper.ITypeHandler handler)
            {
                SqlMapper.AddTypeHandler(targetType, handler);
            }
        }
    }
}