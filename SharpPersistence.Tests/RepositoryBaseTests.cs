using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Microsoft.Extensions.DependencyInjection;
using SharpPersistence.Abstractions;

namespace SharpPersistence.Tests;

public class TestEntity
{
    public int Id { get; set; }
    [MaxLength(10000)] public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}

public class TestDbContext : DbContext
{
    public DbSet<TestEntity> TestEntities { get; set; }

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }
}

public interface ITestRepository : IRepositoryBase<TestEntity>
{
}

public class TestRepository : RepositoryBase<TestEntity, TestDbContext>, ITestRepository
{
    public TestRepository(TestDbContext context) : base(context)
    {
    }
}

public class RepositoryBaseTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly TestDbContext _dbContext;
    private readonly ITestRepository _repository;
    private readonly ServiceProvider _serviceProvider;

    public RepositoryBaseTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();

        services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ITestRepository, TestRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<TestDbContext>();
        _repository = _serviceProvider.GetRequiredService<ITestRepository>();

        _dbContext.Database.EnsureCreated();
    }

    public async ValueTask InitializeAsync()
    {
        _dbContext.TestEntities.RemoveRange(_dbContext.TestEntities);

        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var seed = new List<TestEntity>
        {
            new() { Name = "A", Value = 1 },
            new() { Name = "B", Value = 2 },
            new() { Name = "C", Value = 3 },
            new() { Name = "D", Value = 4 },
        };

        await _repository.CreateManyAsync(seed);

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
    public async Task CreateAsync_AddsEntity()
    {
        var entity = new TestEntity { Name = "E", Value = 5 };

        await _repository.CreateAsync(entity);

        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.TestEntities.Count().ShouldBe(5);
    }

    [Fact]
    public async Task CreateManyAsync_AddsEntities()
    {
        var entities = new[] { new TestEntity { Name = "F", Value = 6 }, new TestEntity { Name = "G", Value = 7 } };

        await _repository.CreateManyAsync(entities);

        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.TestEntities.Count().ShouldBe(6);
    }

    [Fact]
    public async Task GetOneAsync_ReturnsCorrectEntity()
    {
        var entity = await _repository.GetOneAsync(e => e.Name == "A", TestContext.Current.CancellationToken);

        entity.ShouldNotBeNull();
        entity.Value.ShouldBe(1);
    }

    [Fact]
    public async Task GetOneAsync_WithTracking()
    {
        var entity = await _repository.GetOneAsync(e => e.Name == "A", true, TestContext.Current.CancellationToken);

        entity.ShouldNotBeNull();
        _dbContext.Entry(entity).State.ShouldBe(EntityState.Unchanged);
    }

    [Fact]
    public async Task GetOneSubsetAsync_ReturnsProjection()
    {
        var value = await _repository.GetOneSubsetAsync(
            condition: e => e.Name == "A",
            subsetSelector: e => e.Value,
            cancellationToken: TestContext.Current.CancellationToken);

        value.ShouldBe(1);
    }

    [Fact]
    public async Task GetOneSubsetAsync_SelectorAndCancellationTokenOnly_ReturnsFirstProjection()
    {
        // Should return the Value of the first entity (Name = "A", Value = 1)
        var value = await _repository.GetOneSubsetAsync(e => e.Value, TestContext.Current.CancellationToken);
        value.ShouldBe(1);
    }

    [Fact]
    public async Task GetAllSortedAndPaginatedSubsetAsync_ReturnsCorrectPage()
    {
        var result = await _repository.GetAllSortedAndPaginatedSubsetAsync<string, int>
            (1, 2, e => e.Name, (e => e.Value, false), TestContext.Current.CancellationToken);

        result.Count.ShouldBe(2);
        result.First().ShouldBe("A");
    }

    [Fact]
    public async Task GetAllSortedAndPaginatedSubsetAsync_WithConditionAndTracking()
    {
        var result = await _repository.GetAllSortedAndPaginatedSubsetAsync<string, int>(
            page: 1, limit: 2,
            condition: e => e.Value > 1,
            subsetSelector: e => e.Name,
            sorter: (e => e.Value, false),
            enableTracking: true,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Count.ShouldBe(2);
        result.ShouldContain("B");
    }

    [Fact]
    public async Task GetAllSortedAndPaginatedAsync_ReturnsCorrectPage()
    {
        var result = await _repository.GetAllSortedAndPaginatedAsync(1, 2, (e => e.Value, false),
            TestContext.Current.CancellationToken);

        result.Count.ShouldBe(2);
        result.First().Name.ShouldBe("A");
    }

    [Fact]
    public async Task GetAllSortedAndPaginatedAsync_WithConditionAndTracking()
    {
        var result = await _repository.GetAllSortedAndPaginatedAsync(
            page: 1,
            limit: 2,
            condition: e => e.Value > 1,
            sorter: (e => e.Value, false),
            enableTracking: true,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Count.ShouldBe(2);
        result.All(e => e.Value > 1).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllSortedSubsetAsync_ReturnsSortedSubset()
    {
        var result = await _repository.GetAllSortedSubsetAsync<string, int>(e => e.Name, (e => e.Value, true),
            TestContext.Current.CancellationToken);

        result.Count.ShouldBe(4);
        result.First().ShouldBe("D");
    }

    [Fact]
    public async Task GetAllSortedSubsetAsync_WithConditionAndTracking()
    {
        var result = await _repository.GetAllSortedSubsetAsync<string, int>(
            condition: e => e.Value > 2,
            subsetSelector: e => e.Name,
            sorter: (e => e.Value, false),
            enableTracking: true,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Count.ShouldBe(2);
        result.ShouldContain("C");
    }

    [Fact]
    public async Task GetAllSortedAsync_ReturnsSorted()
    {
        var result =
            await _repository.GetAllSortedAsync((e => e.Value, true), TestContext.Current.CancellationToken);

        result.Count.ShouldBe(4);
        result.First().Name.ShouldBe("D");
    }

    [Fact]
    public async Task GetAllSortedAsync_WithConditionAndTracking()
    {
        var result = await _repository.GetAllSortedAsync(
            condition: e => e.Value > 2,
            sorter: (e => e.Value, false),
            enableTracking: true,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Count.ShouldBe(2);
        result.All(e => e.Value > 2).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllSubsetAsync_ReturnsSubset()
    {
        var result = await _repository.GetAllSubsetAsync(e => e.Name, TestContext.Current.CancellationToken);

        result.Count.ShouldBe(4);
        result.ShouldContain("A");
    }

    [Fact]
    public async Task GetAllSubsetAsync_WithConditionAndTracking()
    {
        var result = await _repository.GetAllSubsetAsync(
            condition: e => e.Value > 2,
            subsetSelector: e => e.Name,
            enableTracking: true,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Count.ShouldBe(2);
        result.ShouldContain("C");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAll()
    {
        var result = await _repository.GetAllAsync(TestContext.Current.CancellationToken);

        result.Count.ShouldBe(4);
    }

    [Fact]
    public async Task GetAllAsync_WithConditionAndTracking()
    {
        var result = await _repository.GetAllAsync(e => e.Value > 2, true, TestContext.Current.CancellationToken);

        result.Count.ShouldBe(2);
        result.All(e => e.Value > 2).ShouldBeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrueIfExists()
    {
        var exists = await _repository.ExistsAsync(e => e.Name == "A", TestContext.Current.CancellationToken);

        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task EveryAsync_ReturnsTrueIfAllMatch()
    {
        var all = await _repository.EveryAsync(e => e.Value > 0, TestContext.Current.CancellationToken);

        all.ShouldBeTrue();
    }

    [Fact]
    public async Task GetCountAsync_ReturnsCount()
    {
        var count = await _repository.GetCountAsync(TestContext.Current.CancellationToken);

        count.ShouldBe(4);
    }

    [Fact]
    public async Task GetCountAsync_WithCondition()
    {
        var count = await _repository.GetCountAsync(e => e.Value > 2, TestContext.Current.CancellationToken);

        count.ShouldBe(2);
    }

    [Fact]
    public async Task Remove_RemovesEntity()
    {
        _dbContext.ChangeTracker.Clear();

        var entity = await _repository.GetOneAsync(e => e.Name == "A", TestContext.Current.CancellationToken);

        entity.ShouldNotBeNull();

        _repository.Remove(entity);

        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await _repository.ExistsAsync(e => e.Name == "A", TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task RemoveMany_RemovesEntities()
    {
        _dbContext.ChangeTracker.Clear();

        var entities = (await _repository.GetAllAsync(e => e.Value > 2, TestContext.Current.CancellationToken))
            .ToList();

        _repository.RemoveMany(entities);

        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await _repository.GetCountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task RemoveManyDirectAsync_RemovesEntities()
    {
        _dbContext.ChangeTracker.Clear();

        var removed = await _repository.RemoveManyDirectAsync(e => e.Value > 2);

        removed.ShouldBe(2);

        (await _repository.GetCountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task Update_UpdatesEntity()
    {
        _dbContext.ChangeTracker.Clear();

        var entity = await _repository.GetOneAsync(e => e.Name == "A", TestContext.Current.CancellationToken);

        entity.ShouldNotBeNull();

        entity.Value = 100;

        _repository.Update(entity);

        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await _repository.GetOneAsync(e => e.Name == "A", TestContext.Current.CancellationToken))?.Value.ShouldBe(100);
    }

    [Fact]
    public async Task UpdateMany_UpdatesEntities()
    {
        _dbContext.ChangeTracker.Clear();

        var entities = (await _repository.GetAllAsync(e => e.Value > 2, TestContext.Current.CancellationToken))
            .ToList();

        foreach (var e in entities) e.Value += 10;

        _repository.UpdateMany(entities);

        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await _repository.GetOneAsync(e => e.Name == "C", TestContext.Current.CancellationToken))?.Value.ShouldBe(13);
    }

    [Fact]
    public void TrackEntity_AttachesEntity()
    {
        var entity = new TestEntity { Id = 999, Name = "Z", Value = 99 };

        _repository.TrackEntity(entity);

        _dbContext.Entry(entity).State.ShouldBe(EntityState.Unchanged);
    }

    [Fact]
    public async Task GetOneAsync_WithCancellationToken()
    {
        var entity = await _repository.GetOneAsync(e => e.Name == "A", TestContext.Current.CancellationToken);

        entity.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetOneAsync_WithEnableTracking()
    {
        var entity = await _repository.GetOneAsync(e => e.Name == "A", true, TestContext.Current.CancellationToken);

        entity.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetOneSubsetAsync_AllParams()
    {
        var value = await _repository.GetOneSubsetAsync(
            condition: e => e.Name == "A",
            subsetSelector: e => e.Value,
            enableTracking: true,
            cancellationToken: TestContext.Current.CancellationToken);

        value.ShouldBe(1);
    }

    [Fact]
    public async Task GetOneSubsetAsync_WithCancellationToken()
    {
        var value = await _repository.GetOneSubsetAsync(
            condition: e => e.Name == "A",
            subsetSelector: e => e.Value,
            cancellationToken: TestContext.Current.CancellationToken);

        value.ShouldBe(1);
    }

    [Fact]
    public async Task GetAllSortedAndPaginatedSubsetAsync_Basic()
    {
        var entity = await _repository.GetAllSortedAndPaginatedSubsetAsync<string, int>(1, 2, e => e.Name,
            (e => e.Value, false), TestContext.Current.CancellationToken);

        entity.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetAllSortedAndPaginatedSubsetAsync_WithCondition()
    {
        var entity = await _repository.GetAllSortedAndPaginatedSubsetAsync<string, int>(
            page: 1,
            limit: 2,
            condition: e => e.Value > 1,
            subsetSelector: e => e.Name,
            sorter: (e => e.Value, false),
            cancellationToken: TestContext.Current.CancellationToken);

        entity.All(x => x != "A").ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllSortedAndPaginatedSubsetAsync_WithEnableTracking()
    {
        var entity = await _repository.GetAllSortedAndPaginatedSubsetAsync<string, int>(1, 2, e => e.Name,
            (e => e.Value, false), true, TestContext.Current.CancellationToken);

        entity.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetAllSortedAndPaginatedSubsetAsync_WithConditionAndEnableTracking()
    {
        var entity = await _repository.GetAllSortedAndPaginatedSubsetAsync<string, int>(
            page: 1,
            limit: 2,
            condition: e => e.Value > 1,
            subsetSelector: e => e.Name,
            sorter: (e => e.Value, false),
            enableTracking: true,
            cancellationToken: TestContext.Current.CancellationToken);

        entity.All(x => x != "A").ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllSortedAndPaginatedAsync_Basic()
    {
        var entity = await _repository.GetAllSortedAndPaginatedAsync(1, 2, (e => e.Value, false),
            TestContext.Current.CancellationToken);

        entity.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetAllSortedAndPaginatedAsync_WithCondition()
    {
        var entity = await _repository.GetAllSortedAndPaginatedAsync(
            page: 1,
            limit: 2,
            condition: e => e.Value > 1,
            sorter: (e => e.Value, false),
            cancellationToken: TestContext.Current.CancellationToken);

        entity.All(x => x.Value > 1).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllSortedAndPaginatedAsync_WithEnableTracking()
    {
        var entity = await _repository.GetAllSortedAndPaginatedAsync(1, 2, (e => e.Value, false), true,
            TestContext.Current.CancellationToken);

        entity.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetAllSortedAndPaginatedAsync_WithConditionAndEnableTracking()
    {
        var entity = await _repository.GetAllSortedAndPaginatedAsync(
            page: 1,
            limit: 2,
            condition: e => e.Value > 1,
            sorter: (e => e.Value, false),
            enableTracking: true,
            cancellationToken: TestContext.Current.CancellationToken);

        entity.All(x => x.Value > 1).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllSortedSubsetAsync_Basic()
    {
        var entity = await _repository.GetAllSortedSubsetAsync<string, int>(e => e.Name, (e => e.Value, false),
            TestContext.Current.CancellationToken);

        entity.Count.ShouldBe(4);
    }

    [Fact]
    public async Task GetAllSortedSubsetAsync_WithCondition()
    {
        var entity = await _repository.GetAllSortedSubsetAsync<string, int>(
            condition: e => e.Value > 1,
            subsetSelector: e => e.Name,
            sorter: (e => e.Value, false),
            cancellationToken: TestContext.Current.CancellationToken);

        entity.All(x => x != "A").ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllSortedSubsetAsync_WithEnableTracking()
    {
        var entity = await _repository.GetAllSortedSubsetAsync<string, int>(e => e.Name, (e => e.Value, false), true,
            TestContext.Current.CancellationToken);

        entity.Count.ShouldBe(4);
    }

    [Fact]
    public async Task GetAllSortedSubsetAsync_WithConditionAndEnableTracking()
    {
        var entity = await _repository.GetAllSortedSubsetAsync<string, int>(
            condition: e => e.Value > 1,
            subsetSelector: e => e.Name,
            sorter: (e => e.Value, false),
            enableTracking: true,
            cancellationToken: TestContext.Current.CancellationToken);

        entity.All(x => x != "A").ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllSortedAsync_WithConditionAndEnableTracking()
    {
        var entity = await _repository.GetAllSortedAsync(
            condition: e => e.Value > 1,
            sorter: (e => e.Value, false),
            enableTracking: true,
            cancellationToken: TestContext.Current.CancellationToken);

        entity.All(x => x.Value > 1).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllSortedAsync_WithCondition()
    {
        var entity = await _repository.GetAllSortedAsync(
            condition: e => e.Value > 1,
            sorter: (e => e.Value, false),
            cancellationToken: TestContext.Current.CancellationToken);

        entity.All(x => x.Value > 1).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllSortedAsync_WithEnableTracking()
    {
        var entity = await _repository
            .GetAllSortedAsync((e => e.Value, false), true, TestContext.Current.CancellationToken);

        entity.Count.ShouldBe(4);
    }

    [Fact]
    public async Task GetAllSortedAsync_Basic()
    {
        var entity = await _repository.GetAllSortedAsync((e => e.Value, false), TestContext.Current.CancellationToken);

        entity.Count.ShouldBe(4);
    }

    [Fact]
    public async Task GetAllSubsetAsync_WithConditionAndEnableTracking()
    {
        var entity = await _repository.GetAllSubsetAsync(
            condition: e => e.Value > 1,
            subsetSelector: e => e.Name,
            enableTracking: true,
            cancellationToken: TestContext.Current.CancellationToken);

        entity.All(x => x != "A").ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllSubsetAsync_WithCondition()
    {
        var entity = await _repository.GetAllSubsetAsync(
            condition: e => e.Value > 1,
            subsetSelector: e => e.Name,
            cancellationToken: TestContext.Current.CancellationToken);

        entity.All(x => x != "A").ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllSubsetAsync_WithEnableTracking()
    {
        var entity = await _repository.GetAllSubsetAsync(e => e.Name, true, TestContext.Current.CancellationToken);

        entity.Count.ShouldBe(4);
    }

    [Fact]
    public async Task GetAllSubsetAsync_Basic()
    {
        var entity = await _repository.GetAllSubsetAsync(e => e.Name, TestContext.Current.CancellationToken);

        entity.Count.ShouldBe(4);
    }

    [Fact]
    public async Task GetAllAsync_WithConditionAndEnableTracking()
    {
        var entity = await _repository.GetAllAsync(e => e.Value > 1, true, TestContext.Current.CancellationToken);

        entity.All(x => x.Value > 1).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllAsync_WithCondition()
    {
        var entity = await _repository.GetAllAsync(e => e.Value > 1, TestContext.Current.CancellationToken);

        entity.All(x => x.Value > 1).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllAsync_WithEnableTracking()
    {
        var entity = await _repository.GetAllAsync(true, TestContext.Current.CancellationToken);

        entity.Count.ShouldBe(4);
    }

    [Fact]
    public async Task GetAllAsync_Basic()
    {
        var entity = await _repository.GetAllAsync(TestContext.Current.CancellationToken);

        entity.Count.ShouldBe(4);
    }

    [Fact]
    public async Task GetOneSortedAsync_WithEnableTracking()
    {
        // Get the last entity by Value descending, with enableTracking
        var entity = await _repository.GetOneSortedAsync(e => e.Value > 0, (e => e.Value, true), true,
            TestContext.Current.CancellationToken);

        entity.ShouldNotBeNull();
        entity.Name.ShouldBe("D");
        _dbContext.Entry(entity).State.ShouldBe(EntityState.Unchanged);
    }

    [Fact]
    public async Task GetOneSortedAsync_Basic()
    {
        // Get the last entity by Value descending, without enableTracking param
        var entity = await _repository.GetOneSortedAsync(e => e.Value > 0, (e => e.Value, true),
            TestContext.Current.CancellationToken);

        entity.ShouldNotBeNull();
        entity.Name.ShouldBe("D");
    }

    [Fact]
    public async Task GetOneSortedSubsetAsync_WithEnableTracking_ReturnsProjectedValue()
    {
        // Should return the highest Value (4) with tracking enabled
        var value = await _repository.GetOneSortedSubsetAsync(
            condition: e => e.Value > 0,
            subsetSelector: e => e.Value,
            sorter: (e => e.Value, true),
            enableTracking: true,
            cancellationToken: TestContext.Current.CancellationToken);
        value.ShouldBe(4);
    }

    [Fact]
    public async Task GetOneSortedSubsetAsync_Basic_ReturnsProjectedValue()
    {
        // Should return the highest Value (4) with default tracking (AsNoTracking)
        var value = await _repository.GetOneSortedSubsetAsync(
            condition: e => e.Value > 0,
            subsetSelector: e => e.Value,
            sorter: (e => e.Value, true),
            cancellationToken: TestContext.Current.CancellationToken);
        value.ShouldBe(4);
    }
}