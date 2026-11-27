using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpPersistence.Abstractions.Attributes;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Generators;

namespace SharpPersistence.Migrator.Tests;

public sealed class MigratorUsageAnalyzerTests
{
    private static readonly MetadataReference[] BaseReferences = BuildBaseReferences();

    private static MetadataReference[] BuildBaseReferences()
    {
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];

        var refs = new List<MetadataReference>();
        foreach (var path in trustedAssemblies)
        {
            var fileName = Path.GetFileName(path);
            if (fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase))
            {
                refs.Add(MetadataReference.CreateFromFile(path));
            }
        }

        refs.Add(MetadataReference.CreateFromFile(typeof(IEntityGroup).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(IEntityRegistry).Assembly.Location));
        return [.. refs];
    }

    private static async Task<IReadOnlyList<Diagnostic>> RunAnalyzerAsync(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AnalyzerTestAssembly",
            [
                CSharpSyntaxTree.ParseText("global using System;", parseOptions),
                CSharpSyntaxTree.ParseText(source, parseOptions)
            ],
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var compWithAnalyzers = compilation.WithAnalyzers([new MigratorUsageAnalyzer()]);
        return await compWithAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Spm014_FlagsIsRequiredInsideOptionalOwnsOne_AndAllowsRequiredOwnsOne()
    {
        const string source = """
            using SharpPersistence.Migrator.Abstractions;
            using SharpPersistence.Migrator.Builders;

            namespace OwnsOneDemo;

            public sealed class GeoPoint
            {
                public decimal Latitude { get; set; }
            }

            public sealed class Address
            {
                public string City { get; set; } = string.Empty;
                public GeoPoint? Coordinates { get; set; }
            }

            public sealed class Order
            {
                public long Id { get; set; }
                public Address BillingAddress { get; set; } = new();
                public Address? ShippingAddress { get; set; }
            }

            public sealed class OrderConfig : IEntityConfiguration<Order>
            {
                public void Configure(EntityBuilder<Order> builder)
                {
                    // Allowed: BillingAddress is required
                    builder.OwnsOne(x => x.BillingAddress, a =>
                    {
                        a.Property(x => x.City).IsRequired();
                        // SPM014: nested Coordinates? is optional
                        a.OwnsOne(x => x.Coordinates, g =>
                        {
                            g.Property(x => x.Latitude).IsRequired();
                        });
                    });

                    // SPM014: ShippingAddress? is optional
                    builder.OwnsOne(x => x.ShippingAddress, a =>
                    {
                        a.Property(x => x.City).HasMaxLength(100).IsRequired();
                        a.Property(x => x.City).IsRequired(false); // allowed
                    });
                }
            }
            """;

        var diags = await RunAnalyzerAsync(source);
        var spm014 = diags.Where(d => d.Id == "SPM014").ToList();

        Assert.Equal(2, spm014.Count);
        Assert.Contains(spm014, d => d.GetMessage().Contains("Coordinates"));
        Assert.Contains(spm014, d => d.GetMessage().Contains("ShippingAddress"));
    }

    [Fact]
    public async Task Spm015_FlagsSelfReplacedAndReadOnlyTargetInReplaceEntityTypes()
    {
        const string source = """
            using SharpPersistence.Abstractions.Attributes;
            using SharpPersistence.Migrator.Configuration;

            namespace ReplacementDemo;

            [Entity]
            public sealed class User { public Guid Id { get; set; } }

            [Entity(ReadOnly = true)]
            public sealed class RefUser { public Guid Id { get; set; } }

            [Entity(ReadOnly = true)]
            public sealed class AnotherRef { public Guid Id { get; set; } }

            public static class Wiring
            {
                public static void Setup(DbMigratorOptionsBuilder opt)
                {
                    opt.ReplaceEntityTypes(r => r
                        .Replace<RefUser, User>()         // valid
                        .Replace<User, User>()            // SPM015: self-replace
                        .Replace<RefUser, AnotherRef>()); // SPM015: target is ReadOnly
                }
            }
            """;

        var diags = await RunAnalyzerAsync(source);
        var spm015 = diags.Where(d => d.Id == "SPM015").ToList();

        Assert.Equal(2, spm015.Count);
        Assert.Contains(spm015, d => d.GetMessage().Contains("with itself"));
        Assert.Contains(spm015, d => d.GetMessage().Contains("ReadOnly = true"));
    }
}
