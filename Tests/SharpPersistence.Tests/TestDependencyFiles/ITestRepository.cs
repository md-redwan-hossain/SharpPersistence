using SharpPersistence.Abstractions;

namespace SharpPersistence.Tests.TestDependencyFiles;

public interface ITestRepository : IRepositoryBase<TestEntity>
{
}