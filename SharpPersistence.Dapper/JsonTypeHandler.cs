using System.Data;
using System.Text.Json;
using Dapper;

namespace SharpPersistence.Dapper;

public class JsonTypeHandler<T> : SqlMapper.TypeHandler<T>

{
    private readonly JsonSerializerOptions _options;

    public JsonTypeHandler(JsonSerializerOptions options)
    {
        _options = options;
    }

    public override T? Parse(object value)
    {
        switch (value)
        {
            case null or DBNull:
                break;
            case string str:
                return JsonSerializer.Deserialize<T>(str, _options);
        }

        return default;
    }

    public override void SetValue(IDbDataParameter parameter, T? value)
    {
        parameter.Value = value == null
            ? DBNull.Value
            : JsonSerializer.Serialize(value, _options);

        parameter.DbType = DbType.String;
    }
}