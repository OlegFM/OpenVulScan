using System.CommandLine;
using System.CommandLine.Invocation;

namespace OpenVulScan;

internal sealed class AnalyzeCommand : Command
{
    public AnalyzeCommand()
        : base("analyze", "Analyze code for vulnerabilities")
    {
        var pathArgument = new Argument<string>("path", "Path to .sln, .slnx, or .csproj");
        var formatOption = new Option<string>("--format", () => "sarif", "Output format")
        {
            ArgumentHelpName = "sarif|json|text",
        };
        formatOption.AddValidator(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (value is not null &&
                !new[] { "sarif", "json", "text" }.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                result.ErrorMessage = $"Invalid format '{value}'. Valid values: sarif, json, text.";
            }
        });

        var outputOption = new Option<string?>("--output", () => null, "Output file path");
        var includeOption = new Option<string[]>("--include", "Include glob patterns")
        {
            AllowMultipleArgumentsPerToken = true,
        };
        var excludeOption = new Option<string[]>("--exclude", "Exclude glob patterns")
        {
            AllowMultipleArgumentsPerToken = true,
        };
        var suppressOption = new Option<string?>("--suppress", () => null, "Baseline suppression file");

        AddArgument(pathArgument);
        AddOption(formatOption);
        AddOption(outputOption);
        AddOption(includeOption);
        AddOption(excludeOption);
        AddOption(suppressOption);

        this.SetHandler(async (InvocationContext context) =>
        {
            var path = context.ParseResult.GetValueForArgument(pathArgument);
            var format = context.ParseResult.GetValueForOption(formatOption) ?? "sarif";
            var output = context.ParseResult.GetValueForOption(outputOption);
            var include = context.ParseResult.GetValueForOption(includeOption);
            var exclude = context.ParseResult.GetValueForOption(excludeOption);
            var suppress = context.ParseResult.GetValueForOption(suppressOption);

            var handler = new AnalyzeCommandHandler();

            Stream outputStream;
            if (string.IsNullOrEmpty(output))
            {
                outputStream = Console.OpenStandardOutput();
            }
            else
            {
                outputStream = File.Create(output);
            }

            try
            {
                var result = await handler.ExecuteAsync(
                    new AnalyzeOptions
                    {
                        Path = path,
                        Format = format,
                        Output = output,
                        Include = include,
                        Exclude = exclude,
                        Suppress = suppress,
                    },
                    outputStream,
                    context.GetCancellationToken()).ConfigureAwait(false);

                context.ExitCode = result;
            }
            finally
            {
                if (!string.IsNullOrEmpty(output))
                {
                    await outputStream.DisposeAsync().ConfigureAwait(false);
                }
            }
        });
    }
}
