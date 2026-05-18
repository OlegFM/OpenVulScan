using System.CommandLine;
using System.CommandLine.Invocation;

namespace OpenVulScan;

internal sealed class RulesCommand : Command
{
    public RulesCommand()
        : base("rules", "Manage vulnerability analysis rules")
    {
        AddCommand(new RulesListCommand());
    }
}
