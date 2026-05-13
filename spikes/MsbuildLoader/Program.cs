using System.Diagnostics;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: MsbuildLoader <path-to-solution>");
    Console.WriteLine("Options:");
    Console.WriteLine("  -h, --help    Show this help message");
    return args.Length == 0 ? 1 : 0;
}

string solutionPath = args[0];
if (!File.Exists(solutionPath))
{
    Console.WriteLine($"Solution file not found: {solutionPath}");
    return 1;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var process = Process.GetCurrentProcess();
var stopwatch = Stopwatch.StartNew();

int projectCount = 0;
int compilationCount = 0;
int diagnosticCount = 0;
int totalLoc = 0;
List<string> failedProjects = [];
List<string> workspaceDiagnostics = [];

Console.WriteLine($"Loading solution: {solutionPath}");

try
{
    if (!MSBuildLocator.IsRegistered)
    {
        MSBuildLocator.RegisterDefaults();
    }
    using var workspace = MSBuildWorkspace.Create();
    workspace.RegisterWorkspaceFailedHandler((e) =>
    {
        workspaceDiagnostics.Add(e.Diagnostic.Message);
        Console.WriteLine($"  [WorkspaceDiagnostic] {e.Diagnostic.Kind}: {e.Diagnostic.Message}");
    });

    var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cts.Token).ConfigureAwait(false);

    foreach (var project in solution.Projects)
    {
        cts.Token.ThrowIfCancellationRequested();
        projectCount++;
        Console.WriteLine($"  Project: {project.Name} ({project.FilePath})");

        var compilation = await project.GetCompilationAsync(cts.Token).ConfigureAwait(false);
        if (compilation is null)
        {
            failedProjects.Add(project.Name);
            Console.WriteLine($"    -> Failed to get compilation");
            continue;
        }

        compilationCount++;
        int projectDiagnostics = compilation.GetDiagnostics()
            .Count(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error);
        diagnosticCount += projectDiagnostics;
        Console.WriteLine($"    -> Compilation OK, diagnostics: {projectDiagnostics}");

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            cts.Token.ThrowIfCancellationRequested();
            var text = await syntaxTree.GetTextAsync(cts.Token).ConfigureAwait(false);
            totalLoc += text.Lines.Count;
        }
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine();
    Console.WriteLine("Operation cancelled by user.");
    return 2;
}
#pragma warning disable CA1031
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine($"Failed to open solution: {ex.Message}");
    return 1;
}
#pragma warning restore CA1031

stopwatch.Stop();
process.Refresh();
long peakWorkingSet = process.PeakWorkingSet64;
long managedMemory = GC.GetTotalMemory(false);

Console.WriteLine();
Console.WriteLine("=== Results ===");
Console.WriteLine($"Projects loaded:       {projectCount}");
Console.WriteLine($"Compilations obtained: {compilationCount}");
Console.WriteLine($"Total diagnostics:     {diagnosticCount}");
Console.WriteLine($"Total LoC:             {totalLoc:N0}");
Console.WriteLine($"Elapsed time:          {stopwatch.Elapsed}");
Console.WriteLine($"Peak managed memory:   {managedMemory / 1024 / 1024:N0} MB");
Console.WriteLine($"Peak working set:      {peakWorkingSet / 1024 / 1024:N0} MB");

if (failedProjects.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"Failed to compile projects ({failedProjects.Count}):");
    foreach (var name in failedProjects)
    {
        Console.WriteLine($"  - {name}");
    }
}

if (workspaceDiagnostics.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"Workspace diagnostics ({workspaceDiagnostics.Count}):");
    foreach (var diag in workspaceDiagnostics)
    {
        Console.WriteLine($"  - {diag}");
    }
}

return failedProjects.Count > 0 || workspaceDiagnostics.Count > 0 ? 1 : 0;
