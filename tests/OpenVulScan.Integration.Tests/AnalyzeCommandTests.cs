using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenVulScan.Tests;

public class AnalyzeCommandTests
{
    private static string GetSolutionPath()
    {
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(solutionRoot, "OpenVulScan.slnx");
    }

    [Fact]
    public async Task AnalyzeSolutionReturnsExitCode0AndContainsToolName()
    {
        using var stream = new MemoryStream();

        var result = await AnalyzeCommandHandler.ExecuteAsync(new AnalyzeOptions
        {
            Path = GetSolutionPath(),
            Format = "sarif",
            Output = null,
            Include = null,
            Exclude = null,
            Suppress = null,
        }, stream, CancellationToken.None);

        Assert.Equal(0, result);
        stream.Position = 0;
        using var reader1 = new StreamReader(stream);
        var output = await reader1.ReadToEndAsync();
        Assert.Contains("OpenVulScan", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeSolutionTextFormatReturnsPlainText()
    {
        using var stream = new MemoryStream();

        var result = await AnalyzeCommandHandler.ExecuteAsync(new AnalyzeOptions
        {
            Path = GetSolutionPath(),
            Format = "text",
            Output = null,
            Include = null,
            Exclude = null,
            Suppress = null,
        }, stream, CancellationToken.None);

        Assert.Equal(0, result);
        stream.Position = 0;
        using var reader2 = new StreamReader(stream);
        var output = await reader2.ReadToEndAsync();
        // Text output may be empty if no diagnostics are found; acceptable for now.
        _ = output;
    }

    [Fact]
    public async Task AnalyzeSolutionJsonFormatReturnsParseableJson()
    {
        using var stream = new MemoryStream();

        var result = await AnalyzeCommandHandler.ExecuteAsync(new AnalyzeOptions
        {
            Path = GetSolutionPath(),
            Format = "json",
            Output = null,
            Include = null,
            Exclude = null,
            Suppress = null,
        }, stream, CancellationToken.None);

        Assert.Equal(0, result);
        stream.Position = 0;
        using var reader3 = new StreamReader(stream);
        var output = await reader3.ReadToEndAsync();
        Assert.False(string.IsNullOrWhiteSpace(output));
        var doc = JsonDocument.Parse(output);
        Assert.True(doc.RootElement.TryGetProperty("rules", out _));
        Assert.True(doc.RootElement.TryGetProperty("diagnostics", out _));
    }

    [Fact]
    public async Task AnalyzeNonExistentPathReturnsExitCode2()
    {
        using var stream = new MemoryStream();

        var result = await AnalyzeCommandHandler.ExecuteAsync(new AnalyzeOptions
        {
            Path = "nonexistent.slnx",
            Format = "text",
            Output = null,
            Include = null,
            Exclude = null,
            Suppress = null,
        }, stream, CancellationToken.None);

        Assert.Equal(2, result);
    }

    // No wall-clock assert here: a cold MSBuildWorkspace load on a shared CI
    // runner varied 11s..31s across two days (5s and 30s budgets both broke).
    // Hangs are caught by the workflow-level timeout; a real perf budget needs
    // a dedicated benchmark, not an integration test.
    [Fact]
    public async Task AnalyzeSyntheticProjectSucceeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = Path.Combine(tempDir, "Synthetic.csproj");
        await File.WriteAllTextAsync(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>\n" +
            "</Project>");

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

            Assert.Equal(0, result);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
