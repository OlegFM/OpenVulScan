using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace OpenVulScan;

public sealed class LoadedProject
{
    public string Name { get; }
    public string FilePath { get; }
    public Compilation Compilation { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public LoadedProject(string name, string filePath, Compilation compilation, IReadOnlyList<Diagnostic> diagnostics)
    {
        Name = name;
        FilePath = filePath;
        Compilation = compilation;
        Diagnostics = diagnostics;
    }
}
