namespace OpenVulScan;

public sealed class LoadedSolution
{
    public string Path { get; }
    public IReadOnlyList<LoadedProject> Projects { get; }

    public LoadedSolution(string path, IReadOnlyList<LoadedProject> projects)
    {
        Path = path;
        Projects = projects;
    }
}
