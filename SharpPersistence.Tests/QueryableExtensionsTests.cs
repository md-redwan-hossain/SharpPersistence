using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharpPersistence.Extensions;
using SharpPersistence.Tests.TestDependencyFiles;
using Shouldly;

namespace SharpPersistence.Tests;

public class QueryableExtensionsTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly TestDbContext _dbContext;
    private readonly ServiceProvider _serviceProvider;

    public QueryableExtensionsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();

        services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ITestRepository, TestRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<TestDbContext>();

        _dbContext.Database.EnsureCreated();
    }

    public async ValueTask InitializeAsync()
    {
        await _dbContext.TestEntities.ExecuteDeleteAsync();

        var seed = new List<TestEntity>
        {
            new() { Name = "AB", NumericValue = 11 },
            new() { Name = "BC", NumericValue = 12 },
            new() { Name = "CD", NumericValue = 13 },
            new() { Name = "DE", NumericValue = 14 },
        };

        await _dbContext.AddRangeAsync(seed);

        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        _serviceProvider.Dispose();

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task CursorPaginate()
    {
        var data = await _dbContext.TestEntities.QueryableOffsetPaginate(1, 5)
            .ToListAsync(TestContext.Current.CancellationToken);
        
        data.ShouldNotBeEmpty();
    }
}