using System.Data;
using Dapper;

namespace SharpPersistence.Dapper;

public class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        parameter.DbType = DbType.Date;
    }

    public override DateOnly Parse(object value)
    {
        if (value is DateOnly dateOnly)
        {
            return dateOnly;
        }

        if (value is DateTime dateTime)
        {
            return DateOnly.FromDateTime(dateTime);
        }

        if (value is string str)
        {
            if (DateOnly.TryParse(str, out var parsed))
            {
                return parsed;
            }
        }

        throw new Exception("Invalid value");
    }
}