namespace OpenVulScan;

[Flags]
public enum AnalysisCapability
{
    Ast = 1 << 0,
    Symbol = 1 << 1,
    DataFlow = 1 << 2,
    PathSensitive = 1 << 3,
    Taint = 1 << 4,
    Hierarchy = 1 << 5
}
