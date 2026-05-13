using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: CfgPlay <path-to-cs-file>");
    Console.WriteLine("Options:");
    Console.WriteLine("  -h, --help    Show this help message");
    return args.Length == 0 ? 1 : 0;
}

string filePath = args[0];
if (!File.Exists(filePath))
{
    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "File not found: {0}", filePath));
    return 1;
}

string source = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
var tree = CSharpSyntaxTree.ParseText(source, path: filePath);

var compilation = CSharpCompilation.Create(
    "CfgPlayCompilation",
    new[] { tree },
    new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

var semanticModel = compilation.GetSemanticModel(tree);
var root = await tree.GetRootAsync().ConfigureAwait(false);

var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();

if (methods.Count == 0)
{
    Console.WriteLine("No methods found in the file.");
    return 0;
}

Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Found {0} method(s) in {1}", methods.Count, filePath));
Console.WriteLine(new string('=', 70));

foreach (var method in methods)
{
    var methodSymbol = semanticModel.GetDeclaredSymbol(method);
    if (methodSymbol is null)
    {
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Skipping method without symbol: {0}", method.Identifier.Text));
        continue;
    }

    // Get the root method body operation (no parent) for CFG creation
    var operation = semanticModel.GetOperation(method);

    if (operation is null)
    {
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Skipping method without operation: {0}", methodSymbol.Name));
        continue;
    }

    try
    {
        ControlFlowGraph cfg;
        if (operation is IMethodBodyOperation methodBodyOp)
        {
            cfg = ControlFlowGraph.Create(methodBodyOp);
        }
        else if (operation is IBlockOperation blockOp)
        {
            cfg = ControlFlowGraph.Create(blockOp);
        }
        else
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Skipping unsupported operation type {0} for method: {1}", operation.Kind, methodSymbol.Name));
            continue;
        }

        PrintCfg(methodSymbol.Name, cfg);
    }
    catch (ArgumentException ex)
    {
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Error creating CFG for {0}: {1}", methodSymbol.Name, ex.Message));
    }
}

return 0;

static void PrintCfg(string methodName, ControlFlowGraph cfg)
{
    Console.WriteLine();
    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "=== CFG for method: {0} ===", methodName));
    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Blocks: {0}", cfg.Blocks.Length));
    Console.WriteLine();

    foreach (var block in cfg.Blocks)
    {
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Block [{0}]: {1}", block.Ordinal, block.Kind));

        if (block.Operations.Length > 0)
        {
            Console.WriteLine("  Operations:");
            foreach (var op in block.Operations)
            {
                PrintOperation(op, indent: 4);
            }
        }
        else
        {
            Console.WriteLine("  Operations: (none)");
        }

        if (block.BranchValue is not null)
        {
            Console.WriteLine("  BranchValue:");
            PrintOperation(block.BranchValue, indent: 4);
        }

        if (block.FallThroughSuccessor is not null)
        {
            var successor = block.FallThroughSuccessor;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  FallThroughSuccessor -> [{0}]", successor.Destination?.Ordinal));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "    Semantics: {0}", successor.Semantics));
        }

        if (block.ConditionalSuccessor is not null)
        {
            var successor = block.ConditionalSuccessor;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  ConditionalSuccessor -> [{0}]", successor.Destination?.Ordinal));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "    Semantics: {0}", successor.Semantics));
        }

        if (block.EnclosingRegion is not null)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  EnclosingRegion: {0}", block.EnclosingRegion.Kind));
        }

        Console.WriteLine();
    }

    Console.WriteLine(new string('-', 70));
}

static int GetCaptureIdNumericValue(CaptureId id)
{
    var property = typeof(CaptureId).GetProperty("Id", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    if (property is not null)
    {
        return (int)property.GetValue(id)!;
    }

    var field = typeof(CaptureId).GetField("_id", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    if (field is not null)
    {
        return (int)field.GetValue(id)!;
    }

    return id.GetHashCode();
}

static void PrintOperation(IOperation op, int indent)
{
    var prefix = new string(' ', indent);
    var sb = new StringBuilder();
    sb.Append(prefix);
    sb.Append(op.Kind.ToString());

    if (op is IFlowCaptureOperation capture)
    {
        sb.Append(string.Format(CultureInfo.InvariantCulture, " Id={0}", GetCaptureIdNumericValue(capture.Id)));
    }
    else if (op is IFlowCaptureReferenceOperation captureRef)
    {
        sb.Append(string.Format(CultureInfo.InvariantCulture, " Id={0}", GetCaptureIdNumericValue(captureRef.Id)));
    }
    else if (op is IInstanceReferenceOperation instRef)
    {
        sb.Append(string.Format(CultureInfo.InvariantCulture, " ReferenceKind={0}", instRef.ReferenceKind));
    }

    if (op.Type is not null)
    {
        sb.Append(string.Format(CultureInfo.InvariantCulture, " Type={0}", op.Type.Name));
    }

    Console.WriteLine(sb.ToString());

    foreach (var child in op.ChildOperations)
    {
        PrintOperation(child, indent + 2);
    }
}
