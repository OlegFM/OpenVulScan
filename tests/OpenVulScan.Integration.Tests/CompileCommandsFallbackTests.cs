using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenVulScan.Tests;

public class CompileCommandsFallbackTests
{
    [Fact]
    public async Task FallbackLoadsCorrectlyWithValidCompileCommandsJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = Path.Combine(tempDir, "Test.csproj");
        var sourcePath = Path.Combine(tempDir, "Program.cs");
        var compileCommandsPath = Path.Combine(tempDir, "compile_commands.json");

        await File.WriteAllTextAsync(
            projectPath,
            "<Project Sdk=\"Fake.Sdk/99.0.0\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>");

        await File.WriteAllTextAsync(
            sourcePath,
            "public class Program { public static void Main() { } }");

        var systemRuntimePath = typeof(object).Assembly.Location;
        await File.WriteAllTextAsync(
            compileCommandsPath,
            $$"""
            {
              "compilation": {
                "assemblyName": "TestAssembly",
                "language": "C#",
                "sources": ["Program.cs"],
                "references": ["{{systemRuntimePath.Replace("\\", "\\\\", StringComparison.Ordinal)}}"],
                "preprocessorSymbols": ["DEBUG"]
              }
            }
            """);

        try
        {
            var loader = new ProjectLoader();
            var result = await loader.LoadProjectAsync(projectPath, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("TestAssembly", result.Name);
            Assert.Equal(projectPath, result.FilePath);
            Assert.NotNull(result.Compilation);
            Assert.Contains(result.Compilation.SyntaxTrees, st => st.FilePath == sourcePath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FallbackFailsGracefullyWithoutCompileCommandsJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = Path.Combine(tempDir, "Test.csproj");

        await File.WriteAllTextAsync(
            projectPath,
            "<Project Sdk=\"Fake.Sdk/99.0.0\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>");

        try
        {
            var loader = new ProjectLoader();
            var ex = await Assert.ThrowsAsync<ProjectLoadException>(
                () => loader.LoadProjectAsync(projectPath, CancellationToken.None));

            Assert.Contains("no compile_commands.json fallback", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FallbackResolvesSourcesRelativeToProjectDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var srcDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(srcDir);
        var projectPath = Path.Combine(tempDir, "Test.csproj");
        var sourcePath = Path.Combine(srcDir, "Utils.cs");
        var compileCommandsPath = Path.Combine(tempDir, "compile_commands.json");

        await File.WriteAllTextAsync(
            projectPath,
            "<Project Sdk=\"Fake.Sdk/99.0.0\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>");

        await File.WriteAllTextAsync(
            sourcePath,
            "public class Utils { public static int Add(int a, int b) => a + b; }");

        var systemRuntimePath = typeof(object).Assembly.Location;
        await File.WriteAllTextAsync(
            compileCommandsPath,
            $$"""
            {
              "compilation": {
                "assemblyName": "TestAssembly",
                "language": "C#",
                "sources": ["src/Utils.cs"],
                "references": ["{{systemRuntimePath.Replace("\\", "\\\\", StringComparison.Ordinal)}}"],
                "preprocessorSymbols": []
              }
            }
            """);

        try
        {
            var loader = new ProjectLoader();
            var result = await loader.LoadProjectAsync(projectPath, CancellationToken.None);

            Assert.NotNull(result);
            Assert.NotNull(result.Compilation);
            Assert.Contains(result.Compilation.SyntaxTrees, st =>
                string.Equals(Path.GetFullPath(st.FilePath ?? string.Empty), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void CompileCommandsParserReturnsNullForMissingFile()
    {
        var result = CompileCommandsParser.Parse("nonexistent_compile_commands.json");
        Assert.Null(result);
    }

    [Fact]
    public void CompileCommandsParserReturnsNullForInvalidJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var invalidPath = Path.Combine(tempDir, "compile_commands.json");

        File.WriteAllText(invalidPath, "not valid json");

        try
        {
            var result = CompileCommandsParser.Parse(invalidPath);
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
