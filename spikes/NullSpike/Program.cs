using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace NullSpike;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("Usage: NullSpike <path-to-cs-file>");
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
            "NullSpikeCompilation",
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

                Console.WriteLine();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "=== Null analysis for method: {0} ===", methodSymbol.Name));

                var analysis = new NullStateAnalysis(cfg);
                var finalState = analysis.Analyze();

                if (finalState.IsEmpty)
                {
                    Console.WriteLine("  No local variables tracked.");
                }
                else
                {
                    Console.WriteLine("  Final variable states:");
                    foreach (var kvp in finalState.OrderBy(x => x.Key.Name))
                    {
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "    {0}: {1} ({2})", kvp.Key.Name, kvp.Value, kvp.Value.ToLabel()));
                    }
                }
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Error analyzing {0}: {1}", methodSymbol.Name, ex.Message));
            }
        }

        Console.WriteLine();
        Console.WriteLine(new string('=', 70));
        return 0;
    }
}
