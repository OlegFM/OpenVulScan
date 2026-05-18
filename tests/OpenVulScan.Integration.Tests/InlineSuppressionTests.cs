using System.IO;
using Xunit;

namespace OpenVulScan.Tests;

public class InlineSuppressionTests
{
    [Fact]
    public async Task DisableNextLineSuppressesDiagnostic()
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
            "        // ovs:disable-next-line V3001\n" +
            "        if (a == a) { }\n" +
            "    }\n" +
            "}\n");

        try
        {
            var (diagnostics, _) = await AnalysisRunner.RunAnalysisAsync(projectPath, null, null, CancellationToken.None);
            Assert.DoesNotContain(diagnostics, d => d.Id == "V3001");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task DisableBlockEnableBlockSuppressesDiagnostic()
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
            "        // ovs:disable-block V3001\n" +
            "        if (a == a) { }\n" +
            "        // ovs:enable-block V3001\n" +
            "    }\n" +
            "}\n");

        try
        {
            var (diagnostics, _) = await AnalysisRunner.RunAnalysisAsync(projectPath, null, null, CancellationToken.None);
            Assert.DoesNotContain(diagnostics, d => d.Id == "V3001");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task WithoutSuppressionDiagnosticIsPresent()
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
            var (diagnostics, _) = await AnalysisRunner.RunAnalysisAsync(projectPath, null, null, CancellationToken.None);
            Assert.Contains(diagnostics, d => d.Id == "V3001");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
