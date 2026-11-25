using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using SharpPersistence.Abstractions;
using SharpPersistence.Generators;

namespace SharpPersistence.Tests;

public sealed class SqlStorageExtensionGeneratorTests
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

        refs.Add(MetadataReference.CreateFromFile(typeof(IParsedSqlStorage).Assembly.Location));
        return [.. refs];
    }

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        private readonly SourceText _text = SourceText.From(content);
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }

    private sealed class DictOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) =>
            values.TryGetValue(key, out value);
    }

    private sealed class DictOptionsProvider(Dictionary<string, string> globalValues) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _global = new DictOptions(globalValues);
        private readonly AnalyzerConfigOptions _empty = new DictOptions([]);

        public override AnalyzerConfigOptions GlobalOptions => _global;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _empty;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _empty;
    }

    private static (CSharpCompilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult RunResult)
        RunGenerator(
            IEnumerable<(string Path, string Content)> sqlFiles,
            string? csharpSource = null,
            Dictionary<string, string>? buildProps = null)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText("global using System;", parseOptions)
        };

        if (!string.IsNullOrWhiteSpace(csharpSource))
        {
            trees.Add(CSharpSyntaxTree.ParseText(csharpSource, parseOptions));
        }

        var compilation = CSharpCompilation.Create(
            "Billing.Infrastructure",
            trees,
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        AdditionalText[] additionalTexts = [.. sqlFiles.Select(f => new InMemoryAdditionalText(f.Path, f.Content))];
        IIncrementalGenerator[] generators = [new SqlStorageExtensionGenerator()];

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: generators.Select(GeneratorExtensions.AsSourceGenerator),
            additionalTexts: additionalTexts,
            parseOptions: parseOptions,
            optionsProvider: buildProps is not null ? new DictOptionsProvider(buildProps) : null);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics,
            TestContext.Current.CancellationToken);

        return ((CSharpCompilation)outputCompilation, diagnostics, driver.GetRunResult());
    }

    [Fact]
    public void GeneratesExtensionMethods_WithGetAndSqlConventions_AndCompilesCleanly()
    {
        const string billingSql = """
            -- #start# get_customer_balance
            SELECT COALESCE(SUM(amount), 0)
            FROM billing.ledger_entries
            WHERE customer_id = @customer_id;
            -- #end# get_customer_balance

            -- #start# insert_order
            INSERT INTO sales.orders (id) VALUES (@id);
            -- #end# insert_order

            -- #start# get_balance_sql
            SELECT 1;
            -- #end# get_balance_sql
            """;

        const string reportsSql = """
            -- #start# monthly_revenue
            SELECT 42;
            -- #end# monthly_revenue
            """;

        const string callerCode = """
            using SharpPersistence.Abstractions;
            using MyApp.Billing;

            namespace Caller;

            public static class Usage
            {
                public static string[] ReadAll(IParsedSqlStorage storage) =>
                [
                    storage.GetCustomerBalanceSql(),
                    storage.GetInsertOrderSql(),
                    storage.GetBalanceSql(),
                    storage.GetMonthlyRevenueSql()
                ];
            }
            """;

        var (outputComp, diags, runResult) = RunGenerator(
            [
                (@"D:\Repo\Billing\Queries\Billing.sql", billingSql),
                (@"D:\Repo\Billing\Queries\Reports.sql", reportsSql)
            ],
            csharpSource: callerCode,
            buildProps: new Dictionary<string, string>
            {
                ["build_property.RootNamespace"] = "MyApp.Billing",
                ["build_property.ProjectDir"] = @"D:\Repo\Billing\"
            });

        Assert.Empty(diags);
        Assert.Empty(outputComp.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error));

        var generatedTree = Assert.Single(runResult.GeneratedTrees);
        var generatedCode = generatedTree.GetText(TestContext.Current.CancellationToken).ToString();

        Assert.Contains("namespace MyApp.Billing;", generatedCode);
        Assert.Contains("public static string GetCustomerBalanceSql(this global::SharpPersistence.Abstractions.IParsedSqlStorage storage)", generatedCode);
        Assert.Contains("=> storage[\"get_customer_balance\"];", generatedCode);
        Assert.Contains("public static string GetInsertOrderSql(this global::SharpPersistence.Abstractions.IParsedSqlStorage storage)", generatedCode);
        Assert.Contains("public static string GetBalanceSql(this global::SharpPersistence.Abstractions.IParsedSqlStorage storage)", generatedCode);
        Assert.Contains("public static string GetMonthlyRevenueSql(this global::SharpPersistence.Abstractions.IParsedSqlStorage storage)", generatedCode);
        Assert.Contains("Queries/Billing.sql", generatedCode);
        Assert.DoesNotContain("SqlTags", generatedCode);
    }

    [Fact]
    public void ReportsDiagnostics_ForUnclosedOrphanEmptyDuplicateAndCollidingTags()
    {
        const string badSqlA = """
            -- #start# unclosed_query
            SELECT 1;

            -- #end# orphan_end

            -- #start# empty_query
            -- #end# empty_query

            -- #start# dup_query
            SELECT 1;
            -- #end# dup_query

            -- #start# customer_balance
            SELECT 1;
            -- #end# customer_balance
            """;

        const string badSqlB = """
            -- #start# dup_query
            SELECT 2;
            -- #end# dup_query

            -- #start# get_customer_balance
            SELECT 2;
            -- #end# get_customer_balance
            """;

        var (_, diags, _) = RunGenerator(
            [
                (@"D:\Repo\Queries\A.sql", badSqlA),
                (@"D:\Repo\Queries\B.sql", badSqlB)
            ]);

        var ids = diags.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("SPSQL001", ids);
        Assert.Contains("SPSQL002", ids);
        Assert.Contains("SPSQL003", ids);
        Assert.Contains("SPSQL004", ids);
        Assert.Contains("SPSQL005", ids);
    }

    [Fact]
    public void OptOutProperty_SkipsGeneration()
    {
        const string sql = """
            -- #start# ping
            SELECT 1;
            -- #end# ping
            """;

        var (_, diags, runResult) = RunGenerator(
            [(@"D:\Repo\Queries\Ping.sql", sql)],
            buildProps: new Dictionary<string, string>
            {
                ["build_property.SharpPersistenceGenerateSqlExtensions"] = "false"
            });

        Assert.Empty(diags);
        Assert.Empty(runResult.GeneratedTrees);
    }
}
