using SharpPersistence.Enums;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal static class SqlTypeResolver
{
    public static string Resolve(
        SqlDialect dialect,
        Type clrType,
        string? explicitSqlType,
        int? maxLength,
        int? precision,
        int? scale,
        bool? isUnicode,
        IReadOnlyDictionary<string, PostgresEnumSchema>? enumLookup = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitSqlType))
        {
            return explicitSqlType;
        }

        var t = Nullable.GetUnderlyingType(clrType) ?? clrType;

        return dialect switch
        {
            SqlDialect.PostgreSql => ResolvePostgreSql(t, maxLength, precision, scale, enumLookup),
            SqlDialect.Oracle => ResolveOracle(t, maxLength, precision, scale, isUnicode),
            _ => throw new NotSupportedException($"RDBMS '{dialect}' is not supported yet.")
        };
    }

    private static string ResolvePostgreSql(
        Type t,
        int? maxLength,
        int? precision,
        int? scale,
        IReadOnlyDictionary<string, PostgresEnumSchema>? enumLookup)
    {
        if (t.IsEnum)
        {
            var key = t.FullName ?? t.Name;
            if (enumLookup is not null && enumLookup.TryGetValue(key, out var pgEnum))
            {
                return string.IsNullOrWhiteSpace(pgEnum.Schema)
                    ? $"\"{pgEnum.Name}\""
                    : $"\"{pgEnum.Schema}\".\"{pgEnum.Name}\"";
            }

            return "integer";
        }

        if (t == typeof(string)) return maxLength.HasValue ? $"character varying({maxLength.Value})" : "text";
        if (t == typeof(short) || t == typeof(byte)) return "smallint";
        if (t == typeof(int)) return "integer";
        if (t == typeof(long)) return "bigint";
        if (t == typeof(bool)) return "boolean";
        if (t == typeof(decimal)) return precision.HasValue ? $"numeric({precision},{scale ?? 0})" : "numeric";
        if (t == typeof(double)) return "double precision";
        if (t == typeof(float)) return "real";
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset)) return "timestamp with time zone";
        if (t == typeof(DateOnly)) return "date";
        if (t == typeof(TimeOnly)) return "time without time zone";
        if (t == typeof(TimeSpan)) return "interval";
        if (t == typeof(Guid)) return "uuid";
        if (t == typeof(byte[])) return "bytea";

        return "text";
    }

    private static string ResolveOracle(
        Type t,
        int? maxLength,
        int? precision,
        int? scale,
        bool? isUnicode)
    {
        if (t.IsEnum) return "NUMBER(10)";

        if (t == typeof(string))
        {
            var unicode = isUnicode != false;
            if (maxLength.HasValue)
            {
                return unicode ? $"NVARCHAR2({maxLength.Value})" : $"VARCHAR2({maxLength.Value})";
            }

            return unicode ? "NCLOB" : "CLOB";
        }

        if (t == typeof(byte)) return "NUMBER(3)";
        if (t == typeof(short)) return "NUMBER(5)";
        if (t == typeof(int)) return "NUMBER(10)";
        if (t == typeof(long)) return "NUMBER(19)";
        if (t == typeof(bool)) return "NUMBER(1)";
        if (t == typeof(decimal)) return precision.HasValue ? $"NUMBER({precision},{scale ?? 0})" : "NUMBER(18,2)";
        if (t == typeof(double)) return "BINARY_DOUBLE";
        if (t == typeof(float)) return "BINARY_FLOAT";
        if (t == typeof(DateTime)) return "TIMESTAMP(7)";
        if (t == typeof(DateTimeOffset)) return "TIMESTAMP(7) WITH TIME ZONE";
        if (t == typeof(DateOnly)) return "DATE";
        if (t == typeof(TimeOnly) || t == typeof(TimeSpan)) return "INTERVAL DAY(0) TO SECOND(7)";
        if (t == typeof(Guid)) return "RAW(16)";
        if (t == typeof(byte[])) return "BLOB";

        return isUnicode != false ? "NCLOB" : "CLOB";
    }
}
