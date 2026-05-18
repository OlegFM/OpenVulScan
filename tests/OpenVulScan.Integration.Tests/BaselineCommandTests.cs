using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenVulScan.Tests;

public class BaselineCommandTests
{
    private static string CreateSyntheticProjectWithDiagnostic(string tempDir)
    {
        var projectPath = Path.Combine(tempDir, "Synthetic.csproj");
        File.WriteAllText(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>\n" +
            "</Project>");

        var sourceDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(sourceDir);
        var sourcePath = Path.Combine(sourceDir, "Test.cs");
        File.WriteAllText(
            sourcePath,
            "public class Test\n" +
            "{\n" +
            "    public void M()\n" +
            "    {\n" +
            "        int a = 1;\n" +
            "        if (a == a) { }\n" +
            "    }\n" +
            "}\n");

        return projectPath;
    }

    [Fact]
    public async Task BaselineCreateProducesValidBaselineFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = CreateSyntheticProjectWithDiagnostic(tempDir);
        var baselinePath = Path.Combine(tempDir, "baseline.suppress");

        try
        {
            var result = await BaselineCommandHandler.CreateAsync(projectPath, baselinePath, CancellationToken.None);
            Assert.Equal(0, result);
            Assert.True(File.Exists(baselinePath));

            var lines = await File.ReadAllLinesAsync(baselinePath);
            Assert.Contains(lines, l => l.StartsWith("# OpenVulScan suppression baseline", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.StartsWith("# Generated:", StringComparison.Ordinal));

            var entryLines = lines.Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#')).ToList();
            Assert.NotEmpty(entryLines);

            var parts = entryLines[0].Split('\t');
            Assert.Equal(5, parts.Length);
            Assert.Equal("V3001", parts[0]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task BaselineUpdateAddsNewEntries()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = CreateSyntheticProjectWithDiagnostic(tempDir);
        var baselinePath = Path.Combine(tempDir, "baseline.suppress");

        try
        {
            // Create initial baseline
            var createResult = await BaselineCommandHandler.CreateAsync(projectPath, baselinePath, CancellationToken.None);
            Assert.Equal(0, createResult);
            var initialLines = await File.ReadAllLinesAsync(baselinePath);
            var initialCount = initialLines.Count(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#'));

            // Update should preserve existing and not duplicate
            var updateResult = await BaselineCommandHandler.UpdateAsync(projectPath, baselinePath, CancellationToken.None);
            Assert.Equal(0, updateResult);
            var updatedLines = await File.ReadAllLinesAsync(baselinePath);
            var updatedCount = updatedLines.Count(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#'));

            Assert.Equal(initialCount, updatedCount);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task BaselineDiffShowsCorrectDelta()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = CreateSyntheticProjectWithDiagnostic(tempDir);
        var baselinePath = Path.Combine(tempDir, "baseline.suppress");

        try
        {
            // Create baseline
            var createResult = await BaselineCommandHandler.CreateAsync(projectPath, baselinePath, CancellationToken.None);
            Assert.Equal(0, createResult);

            // Diff against same project should show no differences
            var diffResult = await BaselineCommandHandler.DiffAsync(projectPath, baselinePath, CancellationToken.None);
            Assert.Equal(0, diffResult);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task BaselineIsStableBetweenRuns()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = CreateSyntheticProjectWithDiagnostic(tempDir);
        var baselinePath1 = Path.Combine(tempDir, "baseline1.suppress");
        var baselinePath2 = Path.Combine(tempDir, "baseline2.suppress");

        try
        {
            var result1 = await BaselineCommandHandler.CreateAsync(projectPath, baselinePath1, CancellationToken.None);
            Assert.Equal(0, result1);

            var result2 = await BaselineCommandHandler.CreateAsync(projectPath, baselinePath2, CancellationToken.None);
            Assert.Equal(0, result2);

            var lines1 = (await File.ReadAllLinesAsync(baselinePath1))
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#'))
                .ToList();
            var lines2 = (await File.ReadAllLinesAsync(baselinePath2))
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#'))
                .ToList();

            Assert.Equal(lines1.Count, lines2.Count);
            for (var i = 0; i < lines1.Count; i++)
            {
                // Compare all columns except fingerprint if needed; here we compare full lines
                var parts1 = lines1[i].Split('\t');
                var parts2 = lines2[i].Split('\t');
                Assert.Equal(parts1[0], parts2[0]); // RuleCode
                Assert.Equal(parts1[1], parts2[1]); // FilePath
                Assert.Equal(parts1[2], parts2[2]); // Line
                Assert.Equal(parts1[3], parts2[3]); // Column
                Assert.Equal(parts1[4], parts2[4]); // Fingerprint
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
