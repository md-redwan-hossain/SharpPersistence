using Microsoft.EntityFrameworkCore;

namespace SharpPersistence.Tests.TestDependencyFiles;

public class TestDbContext : DbContext
{
    public const string SoftDeleteFilter = "SoftDelete";

    public DbSet<TestEntity> TestEntities { get; set; }

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

#if NET10_0_OR_GREATER
        modelBuilder.Entity<TestEntity>()
            .HasQueryFilter(SoftDeleteFilter, e => !e.IsDeleted);
#endif
    }
}
