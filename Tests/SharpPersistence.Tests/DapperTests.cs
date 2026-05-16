using SharpPersistence.Dapper;
using SharpPersistence.Tests.TestDependencyFiles;

namespace SharpPersistence.Tests;

public class DapperTests
{
    [Fact]
    public void TestMapping()
    {
        Utils.RegisterJsonTypeHandler<TestDto>();
        Utils.RegisterJsonTypeHandler(typeof(List<TestDto>));
    }
}