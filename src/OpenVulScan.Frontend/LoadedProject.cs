using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace OpenVulScan;

public sealed class LoadedProject
{
    public string Name { get; }
    public string FilePath { get; }
    public Compilation Compilation { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public IReadOnlyList<WorkspaceDiagnostic> WorkspaceDiagnostics { get; }

    public LoadedProject(
        string name,
        string filePath,
        Compilation compilation,
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<WorkspaceDiagnostic>? workspaceDiagnostics = null)
    {
        Name = name;
        FilePath = filePath;
        Compilation = compilation;
        Diagnostics = diagnostics;
        WorkspaceDiagnostics = workspaceDiagnostics ?? new List<WorkspaceDiagnostic>();
    }
}
