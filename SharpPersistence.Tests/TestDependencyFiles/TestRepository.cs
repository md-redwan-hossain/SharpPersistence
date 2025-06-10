using SharpPersistence.EfCore;

namespace SharpPersistence.Tests.TestDependencyFiles;

public class TestRepository : RepositoryBase<TestEntity, TestDbContext>, ITestRepository
{
    public TestRepository(TestDbContext context) : base(context)
    {
    }
}