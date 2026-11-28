using SharpPersistence.Migrator.Cli.Translators;

namespace SharpPersistence.Migrator.Tests;

public sealed class EntityAnnotatorTests
{
    [Fact]
    public void AnnotateDirectory_AnnotatesConcreteTopLevelClasses_SkipsNonEntities_AndIsIdempotent()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sp_annotate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRoot, "Sub"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "bin"));

        try
        {
            var customerPath = Path.Combine(tempRoot, "Customer.cs");
            File.WriteAllText(customerPath, """
                namespace Demo.Domain.Entities;

                public class Customer
                {
                    public int Id { get; set; }

                    public sealed class NestedHelper
                    {
                    }
                }
                """);

            var basePath = Path.Combine(tempRoot, "BaseEntity.cs");
            File.WriteAllText(basePath, """
                namespace Demo.Domain.Entities;

                public abstract class BaseEntity { }
                public static class EntityHelpers { }
                public class GenericWrapper<T> { }
                """);

            var orderPath = Path.Combine(tempRoot, "Sub", "Order.cs");
            File.WriteAllText(orderPath, """
                namespace Demo.Domain.Entities;

                public sealed class Order
                {
                    public int Id { get; set; }
                }
                """);

            File.WriteAllText(Path.Combine(tempRoot, "bin", "Ignored.cs"), """
                namespace Demo.Domain.Entities;
                public class Ignored { }
                """);

            var first = EntityAnnotator.AnnotateDirectory(tempRoot);

            Assert.Empty(first.Warnings);
            Assert.Equal(2, first.Items.Count);
            Assert.All(first.Items, i => Assert.True(i.Modified && !i.AlreadyAnnotated));
            Assert.Contains(first.Items, i => i.EntityName == "Customer");
            Assert.Contains(first.Items, i => i.EntityName == "Order");

            var customerUpdated = File.ReadAllText(customerPath);
            Assert.Contains("using SharpPersistence.Abstractions.Attributes;", customerUpdated);
            Assert.Contains("[Entity]", customerUpdated);
            Assert.DoesNotContain("[Entity]\r\n    public sealed class NestedHelper", customerUpdated);
            Assert.DoesNotContain("[Entity]\n    public sealed class NestedHelper", customerUpdated);

            // Second run must be idempotent
            var second = EntityAnnotator.AnnotateDirectory(tempRoot);
            Assert.Equal(2, second.Items.Count);
            Assert.All(second.Items, i => Assert.True(i.AlreadyAnnotated && !i.Modified));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void AnnotateDirectory_SupportsEntityGroup_ReadOnly_AndDryRun()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sp_annotate_grp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var refUserPath = Path.Combine(tempRoot, "RefUser.cs");
            const string original = """
                namespace Demo.Sales.Entities.Ref;

                public sealed class RefUser
                {
                    public long Id { get; set; }
                }
                """;
            File.WriteAllText(refUserPath, original);

            // Dry run does not write to disk
            var dry = EntityAnnotator.AnnotateDirectory(
                tempRoot,
                entityGroupName: "SalesGroup",
                entityGroupNamespace: "Demo.Sales.Groups",
                readOnly: true,
                dryRun: true);

            var dryItem = Assert.Single(dry.Items);
            Assert.True(dryItem.Modified);
            Assert.Equal(original, File.ReadAllText(refUserPath));

            // Real run writes [Entity<SalesGroup>(ReadOnly = true)] + both usings
            var applied = EntityAnnotator.AnnotateDirectory(
                tempRoot,
                entityGroupName: "SalesGroup",
                entityGroupNamespace: "Demo.Sales.Groups",
                readOnly: true,
                dryRun: false);

            Assert.Single(applied.Items, i => i.EntityName == "RefUser" && i.Modified);
            var updated = File.ReadAllText(refUserPath);
            Assert.Contains("using SharpPersistence.Abstractions.Attributes;", updated);
            Assert.Contains("using Demo.Sales.Groups;", updated);
            Assert.Contains("[Entity<SalesGroup>(ReadOnly = true)]", updated);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ResolveMarkerAttribute_ThrowsWhenGroupNameOrNamespaceMissing()
    {
        Assert.Throws<ArgumentException>(() => EntityAnnotator.ResolveMarkerAttribute("SalesGroup", null));
        Assert.Throws<ArgumentException>(() => EntityAnnotator.ResolveMarkerAttribute(null, "Demo.Sales.Groups"));
    }
}
