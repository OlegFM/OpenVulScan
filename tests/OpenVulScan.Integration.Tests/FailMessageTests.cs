using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenVulScan.Tests;

public class FailMessageTests
{
    [Fact]
    public async Task MissingReferenceProducesV051()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = Path.Combine(tempDir, "Test.csproj");

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <ProjectReference Include=\"NonExistentProject.csproj\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");

        try
        {
            var loader = new ProjectLoader();
            var project = await loader.LoadProjectAsync(projectPath, CancellationToken.None);
            var fails = FailDetector.Detect(project);

            Assert.Contains(fails, f => f.Code == "V051");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task MissingTypeProducesV053()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = Path.Combine(tempDir, "Test.csproj");
        var sourcePath = Path.Combine(tempDir, "Test.cs");

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>\n" +
            "</Project>");

        await File.WriteAllTextAsync(sourcePath,
            "class C\n" +
            "{\n" +
            "    void M()\n" +
            "    {\n" +
            "        var x = new NonExistent.Type();\n" +
            "    }\n" +
            "}\n");

        try
        {
            var loader = new ProjectLoader();
            var project = await loader.LoadProjectAsync(projectPath, CancellationToken.None);
            var fails = FailDetector.Detect(project);

            Assert.Contains(fails, f => f.Code == "V053");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FailsAppearInSarifOutput()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = Path.Combine(tempDir, "Test.csproj");
        var sourcePath = Path.Combine(tempDir, "Test.cs");

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>\n" +
            "</Project>");

        await File.WriteAllTextAsync(sourcePath,
            "class C\n" +
            "{\n" +
            "    void M()\n" +
            "    {\n" +
            "        var x = new NonExistent.Type();\n" +
            "    }\n" +
            "}\n");

        try
        {
            using var stream = new MemoryStream();
            var result = await AnalyzeCommandHandler.ExecuteAsync(new AnalyzeOptions
            {
                Path = projectPath,
                Format = "sarif",
                Output = null,
                Include = null,
                Exclude = null,
                Suppress = null,
            }, stream, CancellationToken.None);

            stream.Position = 0;
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var doc = JsonDocument.Parse(json);
            var runs = doc.RootElement.GetProperty("runs").EnumerateArray().ToList();
            Assert.Single(runs);
            var results = runs[0].GetProperty("results").EnumerateArray().ToList();

            Assert.Contains(results, r => r.GetProperty("ruleId").GetString() == "V053");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FailsAppearInJsonOutput()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = Path.Combine(tempDir, "Test.csproj");
        var sourcePath = Path.Combine(tempDir, "Test.cs");

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>\n" +
            "</Project>");

        await File.WriteAllTextAsync(sourcePath,
            "class C\n" +
            "{\n" +
            "    void M()\n" +
            "    {\n" +
            "        var x = new NonExistent.Type();\n" +
            "    }\n" +
            "}\n");

        try
        {
            using var stream = new MemoryStream();
            var result = await AnalyzeCommandHandler.ExecuteAsync(new AnalyzeOptions
            {
                Path = projectPath,
                Format = "json",
                Output = null,
                Include = null,
                Exclude = null,
                Suppress = null,
            }, stream, CancellationToken.None);

            stream.Position = 0;
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var doc = JsonDocument.Parse(json);
            var fails = doc.RootElement.GetProperty("fails").EnumerateArray().ToList();

            Assert.Contains(fails, f => f.GetProperty("code").GetString() == "V053");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FailsAppearInTextOutput()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = Path.Combine(tempDir, "Test.csproj");
        var sourcePath = Path.Combine(tempDir, "Test.cs");

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>\n" +
            "</Project>");

        await File.WriteAllTextAsync(sourcePath,
            "class C\n" +
            "{\n" +
            "    void M()\n" +
            "    {\n" +
            "        var x = new NonExistent.Type();\n" +
            "    }\n" +
            "}\n");

        try
        {
            using var stream = new MemoryStream();
            var result = await AnalyzeCommandHandler.ExecuteAsync(new AnalyzeOptions
            {
                Path = projectPath,
                Format = "text",
                Output = null,
                Include = null,
                Exclude = null,
                Suppress = null,
            }, stream, CancellationToken.None);

            stream.Position = 0;
            using var reader = new StreamReader(stream);
            var output = await reader.ReadToEndAsync();

            Assert.Contains("V053", output, StringComparison.Ordinal);
            Assert.Contains("FAIL", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
