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
    /// <summary>
    /// Compiles <paramref name="snippet"/> and returns the CFG for its first method declaration,
    /// or for the method named <paramref name="methodName"/> when given. The snippet must contain
    /// at least one regular method body. Constructor bodies and expression-bodied properties are
    /// not supported.
    /// </summary>
    public static (ControlFlowGraph Cfg, SemanticModel Model, IMethodBodyOperation Body) Compile(
        string snippet,
        string? methodName = null)
    {
        var tree = CSharpSyntaxTree.ParseText(snippet);
        var compilation = CSharpCompilation.Create(
            "Test",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .First(m => methodName is null || m.Identifier.ValueText == methodName);
        var op = model.GetOperation(method) ?? throw new InvalidOperationException("No IOperation for method.");
        if (op is not IMethodBodyOperation body)
        {
            throw new InvalidOperationException($"Expected IMethodBodyOperation, got {op.GetType().Name}.");
        }

        // ControlFlowGraph.Create(IMethodBodyOperation) is non-nullable in Roslyn 4.x;
        // a guard here would be dead code (CA1508).
        var cfg = ControlFlowGraph.Create(body);
        return (cfg, model, body);
    }
}
