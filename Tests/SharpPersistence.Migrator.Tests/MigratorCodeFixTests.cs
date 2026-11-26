using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;
using SharpPersistence.Migrator.CodeFixes;
using Shouldly;

namespace SharpPersistence.Migrator.Tests;

public sealed class MigratorCodeFixTests
{
    private static async Task<string> ApplyCodeFixAsync(
        CodeFixProvider provider,
        string source,
        string diagnosticId,
        string targetSnippet,
        int actionIndex = 0)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProject", LanguageNames.CSharp);
        var document = workspace.AddDocument(project.Id, "Test.cs", SourceText.From(source));

        var start = source.IndexOf(targetSnippet, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var span = new TextSpan(start, targetSnippet.Length);
        var tree = await document.GetSyntaxTreeAsync();
        var location = Location.Create(tree!, span);

        var descriptor = new DiagnosticDescriptor(
            diagnosticId, "Test", "Test", "SharpPersistence.Migrator", DiagnosticSeverity.Error, true);
        var diagnostic = Diagnostic.Create(descriptor, location);

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await provider.RegisterCodeFixesAsync(context);
        actions.Count.ShouldBeGreaterThan(actionIndex);

        var operations = await actions[actionIndex].GetOperationsAsync(CancellationToken.None);
        var applyOp = operations.OfType<ApplyChangesOperation>().Single();
        var changedDoc = applyOp.ChangedSolution.GetDocument(document.Id)!;
        var text = await changedDoc.GetTextAsync();
        return text.ToString();
    }

    [Fact]
    public async Task Spm001_MakesRegistrySealedPartialImplementingInterface()
    {
        const string source = """
            [EntityRegistry]
            public class SalesRegistry { }
            """;

        var result = await ApplyCodeFixAsync(new EntityRegistryCodeFixProvider(), source, "SPM001", "SalesRegistry");
        result.ShouldContain("public sealed partial class SalesRegistry : IEntityRegistry");
        result.ShouldContain("using SharpPersistence.Migrator.Abstractions;");
    }

    [Fact]
    public async Task Spm002_FixesConfigurationDeclarationAndAddsParameterlessCtor()
    {
        const string source = """
            [EntityConfiguration<Order>]
            public class OrderConfig
            {
                public OrderConfig(int x) { }
            }
            """;

        var result = await ApplyCodeFixAsync(new EntityRegistryCodeFixProvider(), source, "SPM002", "OrderConfig");
        result.ShouldContain("public sealed class OrderConfig : IEntityConfiguration<Order>");
        result.ShouldContain("public OrderConfig()");
    }

    [Fact]
    public async Task Spm004_AddsConfigureMethod()
    {
        const string source = """
            [EntityRegistry]
            public sealed partial class SalesRegistry : IEntityRegistry
            {
            }
            """;

        var result = await ApplyCodeFixAsync(new EntityRegistryCodeFixProvider(), source, "SPM004", "SalesRegistry");
        result.ShouldContain("public void Configure(IEntityRegistryBuilder registry)");
    }

    [Fact]
    public async Task Spm006_MakesEntityAndContainingClassPublic()
    {
        const string source = """
            internal class Outer
            {
                [Entity]
                private class Order { }
            }
            """;

        var result = await ApplyCodeFixAsync(new EntityRegistryCodeFixProvider(), source, "SPM006", "Order");
        result.ShouldContain("public class Outer");
        result.ShouldContain("public class Order");

        const string groupSource = """
            internal readonly record struct SalesGroup : IEntityGroup;
            """;

        var groupResult = await ApplyCodeFixAsync(new EntityRegistryCodeFixProvider(), groupSource, "SPM006", "SalesGroup");
        groupResult.ShouldContain("public readonly record struct SalesGroup : IEntityGroup;");
    }

    [Fact]
    public async Task Spm008_MakesDbContextSealedPartialInheritingDbContext()
    {
        const string source = """
            [BindEntityRegistry<SalesRegistry>]
            public class SalesDbContext { }
            """;

        var result = await ApplyCodeFixAsync(new DbContextBindingCodeFixProvider(), source, "SPM008", "SalesDbContext");
        result.ShouldContain("public sealed partial class SalesDbContext : DbContext");
        result.ShouldContain("using Microsoft.EntityFrameworkCore;");
    }

    [Fact]
    public async Task Spm009_RemovesConflictingArguments()
    {
        const string source = """
            [BindEntityRegistry<SalesRegistry>]
            [EntityRegistryItem<Order>(Exclude = true, ReadOnly = true)]
            public sealed partial class SalesDbContext : DbContext { }
            """;

        var keepExclude = await ApplyCodeFixAsync(new DbContextBindingCodeFixProvider(), source, "SPM009", "EntityRegistryItem<Order>(Exclude = true, ReadOnly = true)", actionIndex: 0);
        keepExclude.ShouldContain("Exclude = true");
        keepExclude.ShouldNotContain("ReadOnly");

        var removeExclude = await ApplyCodeFixAsync(new DbContextBindingCodeFixProvider(), source, "SPM009", "EntityRegistryItem<Order>(Exclude = true, ReadOnly = true)", actionIndex: 1);
        removeExclude.ShouldContain("ReadOnly = true");
        removeExclude.ShouldNotContain("Exclude");
    }

    [Fact]
    public async Task Spm010_And_Spm011_RemovesAttribute()
    {
        const string source = """
            [EntityRegistryItem<Order>(ReadOnly = true)]
            public sealed partial class SalesDbContext : DbContext { }
            """;

        var result = await ApplyCodeFixAsync(new DbContextBindingCodeFixProvider(), source, "SPM011", "EntityRegistryItem<Order>(ReadOnly = true)");
        result.ShouldNotContain("EntityRegistryItem");
    }

    [Fact]
    public async Task Spm013_ConvertsOnModelCreatingToOnModelCreatingPartial()
    {
        const string source = """
            [BindEntityRegistry<SalesRegistry>]
            public sealed partial class SalesDbContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    base.OnModelCreating(modelBuilder);
                    modelBuilder.HasDefaultSchema("sales");
                }
            }
            """;

        var result = await ApplyCodeFixAsync(new DbContextBindingCodeFixProvider(), source, "SPM013", "OnModelCreating");
        result.ShouldContain("partial void OnModelCreatingPartial(ModelBuilder modelBuilder)");
        result.ShouldNotContain("base.OnModelCreating");
        result.ShouldContain("modelBuilder.HasDefaultSchema(\"sales\");");
    }

    [Fact]
    public async Task Spm014_RemovesIsRequiredFromOptionalOwnedChain()
    {
        const string source = """
            class C
            {
                void M(dynamic a)
                {
                    a.Property(x => x.City).IsRequired().HasMaxLength(50);
                }
            }
            """;

        var result = await ApplyCodeFixAsync(new MigratorUsageCodeFixProvider(), source, "SPM014", "IsRequired()");
        result.ShouldNotContain(".IsRequired()");
        result.ShouldContain("a.Property(x => x.City).HasMaxLength(50);");
    }

    [Fact]
    public async Task Spm015_RemovesInvalidStubMapping()
    {
        const string source = """
            class C
            {
                void M(dynamic stubs)
                {
                    stubs.Map<User, User>();
                }
            }
            """;

        var result = await ApplyCodeFixAsync(new MigratorUsageCodeFixProvider(), source, "SPM015", "Map<User, User>()");
        result.ShouldNotContain("Map<User, User>");
    }
}
