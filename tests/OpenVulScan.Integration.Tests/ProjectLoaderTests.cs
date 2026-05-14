using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenVulScan.Tests;

public class ProjectLoaderTests
{
    [Fact]
    public async Task LoadProjectAsyncNonExistentFileThrowsProjectLoadException()
    {
        var loader = new ProjectLoader();

        var ex = await Assert.ThrowsAsync<ProjectLoadException>(
            () => loader.LoadProjectAsync("nonexistent.csproj", CancellationToken.None));

        Assert.Contains("nonexistent.csproj", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadSolutionAsyncNonExistentFileThrowsProjectLoadException()
    {
        var loader = new ProjectLoader();

        var ex = await Assert.ThrowsAsync<ProjectLoadException>(
            () => loader.LoadSolutionAsync("nonexistent.slnx", CancellationToken.None));

        Assert.Contains("nonexistent.slnx", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadSolutionAsyncCurrentSolutionReturnsProjects()
    {
        var loader = new ProjectLoader();
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var solutionPath = Path.Combine(solutionRoot, "OpenVulScan.slnx");

        var result = await loader.LoadSolutionAsync(solutionPath, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(solutionPath, result.Path);
        Assert.NotEmpty(result.Projects);
        Assert.All(result.Projects, p => Assert.NotNull(p.Compilation));
    }

    [Fact]
    public async Task LoadProjectAsyncSingleCsproj()
    {
        var loader = new ProjectLoader();
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var projectPath = Path.Combine(
            solutionRoot, "src", "OpenVulScan.Core", "OpenVulScan.Core.csproj");

        var result = await loader.LoadProjectAsync(projectPath, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("OpenVulScan.Core", result.Name);
        Assert.Equal(projectPath, result.FilePath);
        Assert.NotNull(result.Compilation);
    }

    [Fact]
    public async Task LoadProjectAsyncMissingSdkThrowsMissingSdkException()
    {
        var loader = new ProjectLoader();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = Path.Combine(tempDir, "Test.csproj");
        await File.WriteAllTextAsync(
            projectPath,
            "<Project Sdk=\"Fake.Sdk/99.0.0\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>");

        try
        {
            var ex = await Assert.ThrowsAsync<MissingSdkException>(
                () => loader.LoadProjectAsync(projectPath, CancellationToken.None));
            Assert.Contains("SDK", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task CancellationTokenRespected()
    {
        var loader = new ProjectLoader();
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var projectPath = Path.Combine(
            solutionRoot, "src", "OpenVulScan.Core", "OpenVulScan.Core.csproj");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => loader.LoadProjectAsync(projectPath, cts.Token));
    }

    [Fact]
    public async Task ThreadSafeConcurrentUsage()
    {
        var loader = new ProjectLoader();
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var projectPath = Path.Combine(
            solutionRoot, "src", "OpenVulScan.Core", "OpenVulScan.Core.csproj");

        var tasks = new List<Task<LoadedProject>>();
        for (int i = 0; i < 3; i++)
        {
            tasks.Add(loader.LoadProjectAsync(projectPath, CancellationToken.None));
        }

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r =>
        {
            Assert.NotNull(r);
            Assert.NotNull(r.Compilation);
        });
    }

    [Fact]
    public void ExceptionTypesCanBeConstructed()
    {
        Assert.NotNull(new ProjectLoadException());
        Assert.NotNull(new ProjectLoadException("message"));
        Assert.NotNull(new ProjectLoadException("message", new InvalidOperationException()));

        Assert.NotNull(new MissingSdkException());
        Assert.NotNull(new MissingSdkException("message"));
        Assert.NotNull(new MissingSdkException("message", new InvalidOperationException()));

        Assert.NotNull(new MissingReferenceException());
        Assert.NotNull(new MissingReferenceException("message"));
        Assert.NotNull(new MissingReferenceException("message", new InvalidOperationException()));
    }
}
