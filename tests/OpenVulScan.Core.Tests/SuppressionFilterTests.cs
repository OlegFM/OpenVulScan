using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests;

public class SuppressionFilterTests
{
    private static Diagnostic CreateDiagnostic(string id, string filePath, int line)
    {
        var lines = new List<string>();
        for (var i = 0; i < line; i++)
        {
            lines.Add("");
        }

        lines.Add("int x = 1;");

        var source = string.Join("\n", lines);
        var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var root = tree.GetRoot();
        var node = root.DescendantNodes().OfType<VariableDeclarationSyntax>().First();

        return Diagnostic.Create(
            new DiagnosticDescriptor(id, "Test", "Test message", "Test", DiagnosticSeverity.Warning, true),
            node.GetLocation());
    }

    [Fact]
    public void DiagnosticInSuppressionRangeIsFiltered()
    {
        var diagnostics = new List<Diagnostic> { CreateDiagnostic("V3001", "test.cs", 5) };
        var suppressions = new List<SuppressionRange>
        {
            new("test.cs", 3, 10, new HashSet<string> { "V3001" })
        };

        var result = SuppressionFilter.Apply(diagnostics, suppressions);

        Assert.Empty(result);
    }

    [Fact]
    public void DiagnosticOutsideSuppressionRangeIsNotFiltered()
    {
        var diagnostics = new List<Diagnostic> { CreateDiagnostic("V3001", "test.cs", 2) };
        var suppressions = new List<SuppressionRange>
        {
            new("test.cs", 3, 10, new HashSet<string> { "V3001" })
        };

        var result = SuppressionFilter.Apply(diagnostics, suppressions);

        Assert.Single(result);
    }

    [Fact]
    public void DiagnosticWithDifferentRuleCodeIsNotFiltered()
    {
        var diagnostics = new List<Diagnostic> { CreateDiagnostic("V3002", "test.cs", 5) };
        var suppressions = new List<SuppressionRange>
        {
            new("test.cs", 3, 10, new HashSet<string> { "V3001" })
        };

        var result = SuppressionFilter.Apply(diagnostics, suppressions);

        Assert.Single(result);
    }

    [Fact]
    public void EmptyRuleCodesSetSuppressesAllRules()
    {
        var diagnostics = new List<Diagnostic>
        {
            CreateDiagnostic("V3001", "test.cs", 5),
            CreateDiagnostic("V3002", "test.cs", 5)
        };
        var suppressions = new List<SuppressionRange>
        {
            new("test.cs", 3, 10, new HashSet<string>())
        };

        var result = SuppressionFilter.Apply(diagnostics, suppressions);

        Assert.Empty(result);
    }

    [Fact]
    public void DiagnosticNotInSourceIsNotFiltered()
    {
        var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor("V3001", "Test", "Test", "Test", DiagnosticSeverity.Warning, true),
            Location.None);
        var diagnostics = new List<Diagnostic> { diagnostic };
        var suppressions = new List<SuppressionRange>
        {
            new("test.cs", 0, 100, new HashSet<string> { "V3001" })
        };

        var result = SuppressionFilter.Apply(diagnostics, suppressions);

        Assert.Single(result);
    }

    [Fact]
    public void DifferentFilePathIsNotFiltered()
    {
        var diagnostics = new List<Diagnostic> { CreateDiagnostic("V3001", "other.cs", 5) };
        var suppressions = new List<SuppressionRange>
        {
            new("test.cs", 3, 10, new HashSet<string> { "V3001" })
        };

        var result = SuppressionFilter.Apply(diagnostics, suppressions);

        Assert.Single(result);
    }
}
