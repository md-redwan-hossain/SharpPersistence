using SharpPersistence.Abstractions;

namespace SharpPersistence.Tests.TestDependencyFiles;

#pragma warning disable CS0618
public interface ITestRepository : IRepositoryBase<TestEntity>
{
}