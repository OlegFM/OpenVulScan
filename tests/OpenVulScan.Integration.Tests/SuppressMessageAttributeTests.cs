using System.IO;
using Xunit;

namespace OpenVulScan.Tests;

public class SuppressMessageAttributeTests
{
    [Fact]
    public async Task MethodLevelSuppressMessageSuppressesDiagnostic()
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
            "using System.Diagnostics.CodeAnalysis;\n" +
            "class C\n" +
            "{\n" +
            "    [SuppressMessage(\"OpenVulScan\", \"V3001\")]\n" +
            "    void M()\n" +
            "    {\n" +
            "        int a = 1;\n" +
            "        if (a == a) { }\n" +
            "    }\n" +
            "}\n");

        try
        {
            var (diagnostics, _, _) = await AnalysisRunner.RunAnalysisAsync(projectPath, null, null, CancellationToken.None);
            Assert.DoesNotContain(diagnostics, d => d.Id == "V3001");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ClassLevelSuppressMessageSuppressesDiagnostic()
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
            "using System.Diagnostics.CodeAnalysis;\n" +
            "[SuppressMessage(\"OpenVulScan\", \"V3001\")]\n" +
            "class C\n" +
            "{\n" +
            "    void M()\n" +
            "    {\n" +
            "        int a = 1;\n" +
            "        if (a == a) { }\n" +
            "    }\n" +
            "}\n");

        try
        {
            var (diagnostics, _, _) = await AnalysisRunner.RunAnalysisAsync(projectPath, null, null, CancellationToken.None);
            Assert.DoesNotContain(diagnostics, d => d.Id == "V3001");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task WrongCategoryDoesNotSuppressDiagnostic()
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
            "using System.Diagnostics.CodeAnalysis;\n" +
            "[SuppressMessage(\"OtherTool\", \"V3001\")]\n" +
            "class C\n" +
            "{\n" +
            "    void M()\n" +
            "    {\n" +
            "        int a = 1;\n" +
            "        if (a == a) { }\n" +
            "    }\n" +
            "}\n");

        try
        {
            var (diagnostics, _, _) = await AnalysisRunner.RunAnalysisAsync(projectPath, null, null, CancellationToken.None);
            Assert.Contains(diagnostics, d => d.Id == "V3001");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task WithoutSuppressMessageDiagnosticIsPresent()
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
            "        int a = 1;\n" +
            "        if (a == a) { }\n" +
            "    }\n" +
            "}\n");

        try
        {
            var (diagnostics, _, _) = await AnalysisRunner.RunAnalysisAsync(projectPath, null, null, CancellationToken.None);
            Assert.Contains(diagnostics, d => d.Id == "V3001");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
