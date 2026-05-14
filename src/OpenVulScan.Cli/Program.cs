using System.CommandLine;

namespace OpenVulScan;

internal sealed class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("OpenVulScan vulnerability scanner");
        rootCommand.AddCommand(new AnalyzeCommand());
        return await rootCommand.InvokeAsync(args).ConfigureAwait(false);
    }
}
