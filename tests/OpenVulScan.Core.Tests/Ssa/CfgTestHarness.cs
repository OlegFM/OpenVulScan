using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan.Tests.Ssa;

internal static class CfgTestHarness
{
    public static (ControlFlowGraph Cfg, SemanticModel Model, IMethodBodyOperation Body) Compile(string snippet)
    {
        var tree = CSharpSyntaxTree.ParseText(snippet);
        var compilation = CSharpCompilation.Create(
            "Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var op = model.GetOperation(method) ?? throw new InvalidOperationException("No IOperation for method.");
        if (op is not IMethodBodyOperation body)
        {
            throw new InvalidOperationException($"Expected IMethodBodyOperation, got {op.GetType().Name}.");
        }

        var cfg = ControlFlowGraph.Create(body);
        return (cfg, model, body);
    }
}
