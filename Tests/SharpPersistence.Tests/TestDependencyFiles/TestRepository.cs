using SharpPersistence.EfCore;

namespace SharpPersistence.Tests.TestDependencyFiles;

#pragma warning disable CS0618
public class TestRepository : RepositoryBase<TestEntity, TestDbContext>, ITestRepository
{
    public TestRepository(TestDbContext context) : base(context)
    {
    }
}