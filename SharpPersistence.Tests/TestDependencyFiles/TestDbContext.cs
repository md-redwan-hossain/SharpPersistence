using Microsoft.EntityFrameworkCore;

namespace SharpPersistence.Tests.TestDependencyFiles;

public class TestDbContext : DbContext
{
    public DbSet<TestEntity> TestEntities { get; set; }

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }
}