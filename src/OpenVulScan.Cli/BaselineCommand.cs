using System.CommandLine;
using System.CommandLine.Invocation;

namespace OpenVulScan;

internal sealed class BaselineCommand : Command
{
    public BaselineCommand()
        : base("baseline", "Manage diagnostic baselines")
    {
        AddCommand(new BaselineCreateCommand());
        AddCommand(new BaselineUpdateCommand());
        AddCommand(new BaselineDiffCommand());
    }
}

internal sealed class BaselineCreateCommand : Command
{
    public BaselineCreateCommand()
        : base("create", "Capture current diagnostics into a baseline file")
    {
        var pathArgument = new Argument<string>("path", "Path to .sln, .slnx, or .csproj");
        var outputOption = new Option<string?>("--output", () => null, "Output baseline file path");

        AddArgument(pathArgument);
        AddOption(outputOption);

        this.SetHandler(async (InvocationContext context) =>
        {
            var path = context.ParseResult.GetValueForArgument(pathArgument);
            var output = context.ParseResult.GetValueForOption(outputOption);

            var result = await BaselineCommandHandler.CreateAsync(path, output, context.GetCancellationToken()).ConfigureAwait(false);
            context.ExitCode = result;
        });
    }
}

internal sealed class BaselineUpdateCommand : Command
{
    public BaselineUpdateCommand()
        : base("update", "Add new diagnostics to an existing baseline file")
    {
        var pathArgument = new Argument<string>("path", "Path to .sln, .slnx, or .csproj");
        var baselineOption = new Option<string?>("--baseline", () => null, "Baseline file path");

        AddArgument(pathArgument);
        AddOption(baselineOption);

        this.SetHandler(async (InvocationContext context) =>
        {
            var path = context.ParseResult.GetValueForArgument(pathArgument);
            var baseline = context.ParseResult.GetValueForOption(baselineOption);

            var result = await BaselineCommandHandler.UpdateAsync(path, baseline, context.GetCancellationToken()).ConfigureAwait(false);
            context.ExitCode = result;
        });
    }
}

internal sealed class BaselineDiffCommand : Command
{
    public BaselineDiffCommand()
        : base("diff", "Show delta between current diagnostics and baseline")
    {
        var pathArgument = new Argument<string>("path", "Path to .sln, .slnx, or .csproj");
        var baselineOption = new Option<string?>("--baseline", () => null, "Baseline file path");

        AddArgument(pathArgument);
        AddOption(baselineOption);

        this.SetHandler(async (InvocationContext context) =>
        {
            var path = context.ParseResult.GetValueForArgument(pathArgument);
            var baseline = context.ParseResult.GetValueForOption(baselineOption);

            var result = await BaselineCommandHandler.DiffAsync(path, baseline, context.GetCancellationToken()).ConfigureAwait(false);
            context.ExitCode = result;
        });
    }
}
