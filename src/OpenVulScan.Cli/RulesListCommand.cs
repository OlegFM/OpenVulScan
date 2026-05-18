using System.CommandLine;
using System.CommandLine.Invocation;

namespace OpenVulScan;

internal sealed class RulesListCommand : Command
{
    public RulesListCommand()
        : base("list", "List all registered vulnerability analysis rules")
    {
        var formatOption = new Option<string>("--format", () => "text", "Output format")
        {
            ArgumentHelpName = "json|text",
        };
        formatOption.AddValidator(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (value is not null &&
                !new[] { "json", "text" }.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                result.ErrorMessage = $"Invalid format '{value}'. Valid values: json, text.";
            }
        });

        var enabledOnlyOption = new Option<bool>("--enabled-only", () => false, "Show only enabled rules");

        AddOption(formatOption);
        AddOption(enabledOnlyOption);

        this.SetHandler(async (InvocationContext context) =>
        {
            var format = context.ParseResult.GetValueForOption(formatOption) ?? "text";
            var enabledOnly = context.ParseResult.GetValueForOption(enabledOnlyOption);

            using var outputStream = Console.OpenStandardOutput();
            var result = await RulesListCommandHandler.ExecuteAsync(format, enabledOnly, outputStream).ConfigureAwait(false);
            context.ExitCode = result;
        });
    }
}
