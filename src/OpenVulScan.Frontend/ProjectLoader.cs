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

        try
        {
            var project = await workspace.OpenProjectAsync(path, cancellationToken: ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null)
            {
                throw new ProjectLoadException($"Failed to get compilation for project: {path}");
            }

            var diagnostics = compilation.GetDiagnostics(ct).ToList();
            var loadedProject = new LoadedProject(project.Name, project.FilePath!, compilation, diagnostics, workspaceDiagnostics);

            if (HasMissingSdk(workspaceDiagnostics))
            {
                var fallbackResult = await TryFallbackAsync(path, ct).ConfigureAwait(false);
                if (fallbackResult is not null)
                {
                    return fallbackResult;
                }
            }

            ThrowIfUnrecoverable(workspaceDiagnostics, path);

            return loadedProject;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var fallbackResult = await TryFallbackAsync(path, ct).ConfigureAwait(false);
            if (fallbackResult is not null)
            {
                return fallbackResult;
            }

            throw new ProjectLoadException($"Failed to load project '{path}' via MSBuild and no compile_commands.json fallback was available.", ex);
        }
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
            LoadedProject? loadedProject = null;

            try
            {
                var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
                if (compilation is null)
                {
                    throw new ProjectLoadException($"Failed to get compilation for project: {project.Name}");
                }

                var diagnostics = compilation.GetDiagnostics(ct).ToList();
                loadedProject = new LoadedProject(project.Name, project.FilePath!, compilation, diagnostics, workspaceDiagnostics);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (project.FilePath is not null)
                {
                    loadedProject = await TryFallbackAsync(project.FilePath, ct).ConfigureAwait(false);
                }

                if (loadedProject is null)
                {
                    throw new ProjectLoadException($"Failed to load project '{project.Name}' in solution '{path}' and no fallback was available.", ex);
                }
            }

            loadedProjects.Add(loadedProject);
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

    private static bool HasMissingSdk(List<WorkspaceDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Kind == WorkspaceDiagnosticKind.Failure &&
                IsMissingSdkMessage(diagnostic.Message))
            {
                return true;
            }
        }

        return false;
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

    private static async Task<LoadedProject?> TryFallbackAsync(string projectPath, CancellationToken ct)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var compileCommandsPath = Path.Combine(projectDirectory, "compile_commands.json");

        var commands = CompileCommandsParser.Parse(compileCommandsPath);
        if (commands is null)
        {
            return null;
        }

        return await AdhocProjectLoader.LoadAsync(projectPath, commands, ct).ConfigureAwait(false);
    }
}
