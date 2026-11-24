using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using WebApiExample.Endpoints;
using WebApiExample.Persistence;

await using var pgContainer = new PostgreSqlBuilder("postgres:18")
    .WithDatabase("webapi_example")
    .WithUsername("postgres")
    .WithPassword("postgres")
    .Build();

await pgContainer.StartAsync();

var connectionString = pgContainer.GetConnectionString();
Console.WriteLine($"PostgreSQL Testcontainer started: {connectionString}");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

await DatabaseSeeder.InitializeAsync(app.Services);

app.MapCategoryEndpoints();
app.MapProductEndpoints();

await app.RunAsync();
