using SharpPersistence.Migrator.Cli;
using SharpPersistence.Migrator.Cli.Config;

namespace SharpPersistence.Migrator.Tests;

public sealed class CliConfigTests
{
    [Fact]
    public void GenerateOrRefresh_ScansSlnx_QualifiesMigratorRoot_AutoSetsSingleDefault_AndExcludesTests()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sp_cli_cfg_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, "src", "AppRoot"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "src", "AppToolbox"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "src", "Sales.Infrastructure"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "tests", "Sales.Tests"));

            File.WriteAllText(
                Path.Combine(tempRoot, "Demo.slnx"),
                """
                <Solution>
                  <Project Path="src/AppRoot/AppRoot.csproj" />
                  <Project Path="src/AppToolbox/AppToolbox.csproj" />
                  <Project Path="src/Sales.Infrastructure/Sales.Infrastructure.csproj" />
                  <Project Path="tests/Sales.Tests/Sales.Tests.csproj" />
                </Solution>
                """);

            File.WriteAllText(
                Path.Combine(tempRoot, "src", "AppRoot", "AppRoot.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <EntityRegistryRoot>true</EntityRegistryRoot>
                  </PropertyGroup>
                </Project>
                """);

            // Random console app without migrator root must NOT qualify as startupProject
            File.WriteAllText(
                Path.Combine(tempRoot, "src", "AppToolbox", "AppToolbox.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
                </Project>
                """);

            File.WriteAllText(
                Path.Combine(tempRoot, "src", "Sales.Infrastructure", "Sales.Infrastructure.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(
                Path.Combine(tempRoot, "src", "Sales.Infrastructure", "SalesRegistry.cs"),
                "[EntityRegistry]\npublic sealed partial class SalesRegistry : IEntityRegistry { }");

            File.WriteAllText(
                Path.Combine(tempRoot, "tests", "Sales.Tests", "Sales.Tests.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
            File.WriteAllText(
                Path.Combine(tempRoot, "tests", "Sales.Tests", "FakeRoot.cs"),
                "[assembly: EntityRegistryRoot]");

            var result = SolutionProjectScanner.GenerateOrRefresh(tempRoot, existingConfig: null);

            Assert.Empty(result.Warnings);
            Assert.Equal("src/AppRoot/AppRoot.csproj", result.Config.DefaultProject);
            Assert.Equal(["src/AppRoot/AppRoot.csproj"], result.Config.Projects);
            Assert.Empty(result.Config.OtherProjects);
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
    public void GenerateOrRefresh_OnRerun_ValidatesOtherProjectsAndDefaultStartupEligibility()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sp_cli_rerun_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, "src", "HostA"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "src", "HostB"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "src", "PlainLib"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "src", "CustomTool"));

            File.WriteAllText(
                Path.Combine(tempRoot, "Demo.slnx"),
                """
                <Solution>
                  <Project Path="src/HostA/HostA.csproj" />
                  <Project Path="src/HostB/HostB.csproj" />
                  <Project Path="src/PlainLib/PlainLib.csproj" />
                  <Project Path="src/CustomTool/CustomTool.csproj" />
                </Solution>
                """);

            File.WriteAllText(Path.Combine(tempRoot, "src", "HostA", "HostA.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
            File.WriteAllText(Path.Combine(tempRoot, "src", "HostA", "Program.cs"), "builder.Services.AddDbMigrator(o => {});");

            File.WriteAllText(Path.Combine(tempRoot, "src", "HostB", "HostB.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
            File.WriteAllText(Path.Combine(tempRoot, "src", "HostB", "Factory.cs"), "public sealed class F : IDesignTimeDbMigratorFactory { }");

            File.WriteAllText(Path.Combine(tempRoot, "src", "PlainLib", "PlainLib.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(tempRoot, "src", "CustomTool", "CustomTool.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            var existing = new MigratorCliConfig
            {
                // PlainLib exists on disk, but is NOT an eligible migrator project
                DefaultProject = "src/PlainLib/PlainLib.csproj",
                OtherProjects =
                [
                    "src/CustomTool/CustomTool.csproj",     // valid manual entry -> preserved
                    "src/Deleted/Deleted.csproj",           // missing -> pruned with warning
                    "src/HostA/HostA.csproj"                // duplicate of projects -> pruned with warning
                ]
            };

            var result = SolutionProjectScanner.GenerateOrRefresh(tempRoot, existing);

            Assert.Null(result.Config.DefaultProject); // cleared (ineligible) and 2 migrator projects exist so no auto-default
            Assert.Equal(["src/CustomTool/CustomTool.csproj"], result.Config.OtherProjects);
            Assert.Equal(["src/HostA/HostA.csproj", "src/HostB/HostB.csproj"], result.Config.Projects);
            Assert.Contains(result.Warnings, w => w.Contains("no longer an eligible migrator project"));
            Assert.Contains(result.Warnings, w => w.Contains("Deleted.csproj"));
            Assert.Contains(result.Warnings, w => w.Contains("duplicate path"));
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
    public void ProjectResolutionService_ThrowsWhenNoConfigAndNoFlags_AndSupportsInteractivePicker()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sp_cli_res_{Guid.NewGuid():N}");
        try
        {
            var nestedDir = Path.Combine(tempRoot, "src", "Sales");
            var hostDir = Path.Combine(tempRoot, "src", "AppRoot");
            Directory.CreateDirectory(nestedDir);
            Directory.CreateDirectory(hostDir);

            var hostProj = Path.Combine(hostDir, "AppRoot.csproj");
            var salesProj = Path.Combine(nestedDir, "Sales.csproj");
            File.WriteAllText(hostProj, "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
            File.WriteAllText(salesProj, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            // 1. No config file & no --project -> fails fast with actionable message
            var noCfgEx = Assert.Throws<InvalidOperationException>(() =>
                ProjectResolutionService.Resolve(new SharedSettings { NonInteractive = true }, startDirectory: nestedDir));
            Assert.Contains("db-migrator config --generate", noCfgEx.Message);

            // 2. Write config with 2 migrator projects and no default -> prompts "Select migrator project"
            var config = new MigratorCliConfig
            {
                Projects = ["src/AppRoot/AppRoot.csproj", "src/Sales/Sales.csproj"]
            };
            config.Save(Path.Combine(tempRoot, MigratorCliConfig.FileName));

            var promptsShown = new List<string>();
            var fakePicker = new FakeProjectPicker((title, choices) =>
            {
                promptsShown.Add(title);
                return choices.First(c => c.RelativePath.Contains("Sales.csproj", StringComparison.OrdinalIgnoreCase));
            });

            var resolved = ProjectResolutionService.Resolve(
                new SharedSettings(),
                startDirectory: nestedDir,
                picker: fakePicker);

            Assert.Equal(Path.GetFullPath(salesProj), resolved);
            Assert.Single(promptsShown);
            Assert.Equal("Select migrator project", promptsShown[0]);

            // 3. When defaultProject is set, zero prompts are shown
            config.DefaultProject = "src/AppRoot/AppRoot.csproj";
            config.Save(Path.Combine(tempRoot, MigratorCliConfig.FileName));
            promptsShown.Clear();

            var resolvedNoPrompt = ProjectResolutionService.Resolve(
                new SharedSettings(),
                startDirectory: nestedDir,
                picker: fakePicker);

            Assert.Empty(promptsShown);
            Assert.Equal(Path.GetFullPath(hostProj), resolvedNoPrompt);

            // 4. Explicit --project bypasses config and prompts
            var resolvedExplicit = ProjectResolutionService.Resolve(
                new SharedSettings { ProjectPath = salesProj },
                startDirectory: nestedDir,
                picker: fakePicker);

            Assert.Empty(promptsShown);
            Assert.Equal(salesProj, resolvedExplicit);
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
    public void SpectreProjectPicker_FormatChoice_SurvivesSearchMarkupRoundTrip()
    {
        var choices = new[]
        {
            new ProjectChoice("AppRoot", "src/AppRoot/AppRoot.csproj", @"D:\repo\src\AppRoot\AppRoot.csproj", "startup"),
            new ProjectChoice("Sales", "src/Sales/Sales.csproj", @"D:\repo\src\Sales\Sales.csproj", "registry"),
            new ProjectChoice("Tools", "src/Tools/Tools.csproj", @"D:\repo\src\Tools\Tools.csproj", "other")
        };

        foreach (var choice in choices)
        {
            var formatted = SpectreProjectPicker.FormatChoice(choice);
            var initial = new Spectre.Console.Markup(formatted);
            Assert.NotNull(initial);

            // Spectre.Console's SelectionPrompt.EnableSearch() strips markup via Markup.Remove()
            // and re-parses the resulting string as Markup when highlighting search matches.
            var stripped = Spectre.Console.Markup.Remove(formatted);
            var reparsed = new Spectre.Console.Markup(stripped);
            Assert.NotNull(reparsed);
        }
    }

    private sealed class FakeProjectPicker(Func<string, IReadOnlyList<ProjectChoice>, ProjectChoice> pick) : IProjectPicker
    {
        public bool IsInteractive(bool nonInteractiveFlag) => !nonInteractiveFlag;
        public ProjectChoice PickProject(string title, IReadOnlyList<ProjectChoice> choices) => pick(title, choices);
    }
}
