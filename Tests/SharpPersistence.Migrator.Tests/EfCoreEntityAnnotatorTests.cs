using SharpPersistence.Migrator.Cli.Translators;
using Xunit;

namespace SharpPersistence.Migrator.Tests;

public sealed class EfCoreEntityAnnotatorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "sp_entity_annotator_" + Guid.NewGuid().ToString("N"));

    public EfCoreEntityAnnotatorTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void AnnotateDirectory_DefaultEntityAttribute_AnnotatesDbContextEntitiesOnly()
    {
        var dbCtxPath = Path.Combine(_tempDir, "AppDbContext.cs");
        File.WriteAllText(dbCtxPath, """
            using App.Domain.Entities;
            using Microsoft.EntityFrameworkCore;

            namespace App.Infra;

            public sealed class AppDbContext : DbContext
            {
                public DbSet<User> Users => Set<User>();
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Role>();
                }
            }
            """);

        var entitiesDir = Path.Combine(_tempDir, "Entities");
        Directory.CreateDirectory(entitiesDir);
        var userPath = Path.Combine(entitiesDir, "User.cs");
        var rolePath = Path.Combine(entitiesDir, "Role.cs");
        var dtoPath = Path.Combine(entitiesDir, "UserDto.cs");

        File.WriteAllText(userPath, "namespace App.Domain.Entities;\n\npublic sealed class User { public long Id { get; set; } }\n");
        File.WriteAllText(rolePath, "namespace App.Domain.Entities;\n\npublic sealed class Role { public long Id { get; set; } }\n");
        File.WriteAllText(dtoPath, "namespace App.Domain.Entities;\n\npublic sealed class UserDto { public long Id { get; set; } }\n");

        var result = EfCoreEntityAnnotator.AnnotateDirectory(dbCtxPath, entitiesDir);

        Assert.Equal("Entity", result.MarkerAttribute.AttributeShortName);
        Assert.Equal(2, result.Items.Count);
        Assert.Empty(result.UnmatchedEntityNames);
        Assert.Contains("using SharpPersistence.Abstractions.Attributes;", File.ReadAllText(userPath));
        Assert.Contains("[Entity]", File.ReadAllText(userPath));
        Assert.Contains("[Entity]", File.ReadAllText(rolePath));
        Assert.DoesNotContain("[Entity]", File.ReadAllText(dtoPath));

        // Re-running is idempotent
        var second = EfCoreEntityAnnotator.AnnotateDirectory(dbCtxPath, entitiesDir);
        Assert.All(second.Items, item => Assert.True(item.AlreadyAnnotated));
        Assert.All(second.Items, item => Assert.False(item.Modified));
    }

    [Fact]
    public void AnnotateDirectory_EntityGroupNameAndNamespace_ReadOnlyAndDryRun()
    {
        var dbCtxPath = Path.Combine(_tempDir, "AccessControlDbContext.cs");
        File.WriteAllText(dbCtxPath, """
            using AccessControlModule.Domain.Entities;
            using Microsoft.EntityFrameworkCore;

            namespace AccessControlModule.Infra;

            public sealed class AccessControlDbContext : DbContext
            {
                public DbSet<User> Users => Set<User>();
                public DbSet<MissingStub> Stubs => Set<MissingStub>();
            }
            """);

        var entitiesDir = Path.Combine(_tempDir, "Entities");
        Directory.CreateDirectory(entitiesDir);
        var userPath = Path.Combine(entitiesDir, "User.cs");
        const string originalUser = """
            namespace AccessControlModule.Domain.Entities;

            /// <summary>User entity.</summary>
            public sealed class User
            {
                public long Id { get; set; }
            }
            """;
        File.WriteAllText(userPath, originalUser);

        var dry = EfCoreEntityAnnotator.AnnotateDirectory(
            dbCtxPath,
            entitiesDir,
            entityGroupName: "AccessControlGroup",
            entityGroupNamespace: "AccessControlModule.Domain",
            readOnly: true,
            dryRun: true);
        Assert.Equal("Entity<AccessControlGroup>", dry.MarkerAttribute.AttributeShortName);
        Assert.Equal("SharpPersistence.Abstractions.Attributes", dry.MarkerAttribute.AttributeNamespace);
        Assert.Equal("AccessControlModule.Domain", dry.MarkerAttribute.GroupNamespace);
        Assert.Single(dry.Items);
        Assert.True(dry.Items[0].Modified);
        Assert.Equal(["MissingStub"], dry.UnmatchedEntityNames);
        Assert.Equal(originalUser, File.ReadAllText(userPath));

        var applied = EfCoreEntityAnnotator.AnnotateDirectory(
            dbCtxPath,
            entitiesDir,
            entityGroupName: "AccessControlGroup",
            entityGroupNamespace: "AccessControlModule.Domain",
            readOnly: true,
            dryRun: false);
        Assert.Single(applied.Items);
        var updated = File.ReadAllText(userPath);
        Assert.Contains("using SharpPersistence.Abstractions.Attributes;", updated);
        Assert.Contains("using AccessControlModule.Domain;", updated);
        Assert.Contains("/// <summary>User entity.</summary>", updated);
        Assert.Contains("[Entity<AccessControlGroup>(ReadOnly = true)]", updated);
    }
}
