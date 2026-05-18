using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace OpenVulScan;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1822:Mark members as static",
    Justification = "Instance API for future extensibility")]
public class ProjectLoader
{
    private static readonly Lock RegistrationLock = new();
    private static bool _isRegistered;

    public async Task<LoadedProject> LoadProjectAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            throw new ProjectLoadException($"Project file not found: {path}");
        }

        EnsureMsBuildRegistered();

        var workspaceDiagnostics = new List<WorkspaceDiagnostic>();
        using var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            workspaceDiagnostics.Add(e.Diagnostic);
        });

        var project = await workspace.OpenProjectAsync(path, cancellationToken: ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation is null)
        {
            throw new ProjectLoadException($"Failed to get compilation for project: {path}");
        }

        var diagnostics = compilation.GetDiagnostics(ct).ToList();
        var loadedProject = new LoadedProject(project.Name, project.FilePath!, compilation, diagnostics, workspaceDiagnostics);

        ThrowIfUnrecoverable(workspaceDiagnostics, path);

        return loadedProject;
    }

    public async Task<LoadedSolution> LoadSolutionAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            throw new ProjectLoadException($"Solution file not found: {path}");
        }

        EnsureMsBuildRegistered();

        var workspaceDiagnostics = new List<WorkspaceDiagnostic>();
        using var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            workspaceDiagnostics.Add(e.Diagnostic);
        });

        var solution = await workspace.OpenSolutionAsync(path, cancellationToken: ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var loadedProjects = new List<LoadedProject>();
        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null)
            {
                throw new ProjectLoadException($"Failed to get compilation for project: {project.Name}");
            }

            var diagnostics = compilation.GetDiagnostics(ct).ToList();
            loadedProjects.Add(new LoadedProject(project.Name, project.FilePath!, compilation, diagnostics, workspaceDiagnostics));
        }

        ThrowIfUnrecoverable(workspaceDiagnostics, path);

        return new LoadedSolution(path, loadedProjects);
    }

    private static void EnsureMsBuildRegistered()
    {
        if (_isRegistered)
        {
            return;
        }

        lock (RegistrationLock)
        {
            if (_isRegistered)
            {
                return;
            }

            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
            }

            _isRegistered = true;
        }
    }

    private static void ThrowIfUnrecoverable(List<WorkspaceDiagnostic> diagnostics, string path)
    {
        foreach (var diagnostic in diagnostics)
        {
            var message = diagnostic.Message;

            if (diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                if (IsMissingSdkMessage(message))
                {
                    throw new MissingSdkException($"Missing SDK when loading '{path}': {message}");
                }

                // Missing references and other workspace failures are captured as fails (V051/V052)
                // instead of throwing, so analysis can continue where possible.
            }
        }
    }

    private static bool IsMissingSdkMessage(string message)
    {
        if (!message.Contains("SDK", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("could not", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("не удалось", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("не найден", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissingReferenceMessage(string message)
    {
        if (!message.Contains("reference", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("could not", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("failed", StringComparison.OrdinalIgnoreCase);
    }
}
