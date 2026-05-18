using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests;

public class JsonEmitterTests
{
    [Fact]
    public async Task WriteAsyncWithNoDiagnosticsAndNoFailsReturnsEmptyCollections()
    {
        using var stream = new MemoryStream();
        var rules = new List<RuleDescriptor>();
        var diagnostics = new List<Diagnostic>();
        var fails = new List<AnalysisFail>();

        await JsonEmitter.WriteAsync(diagnostics, fails, rules, stream, CancellationToken.None);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(0, root.GetProperty("rules").GetArrayLength());
        Assert.Equal(0, root.GetProperty("diagnostics").GetArrayLength());
        Assert.Equal(0, root.GetProperty("fails").GetArrayLength());
    }

    [Fact]
    public async Task WriteAsyncWithOneDiagnosticWithLocation()
    {
        using var stream = new MemoryStream();
        var rules = new List<RuleDescriptor>
        {
            new("V3001", RuleSeverity.Level1, "CWE-571", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast, typeof(DummyRule))
        };

        var source = "class C { void M() {} }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "src/Foo.cs");
        var root = await tree.GetRootAsync();
        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
        var location = classDecl.Identifier.GetLocation();

        var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor("V3001", "Title", "Message", "Category", DiagnosticSeverity.Warning, true),
            location);

        var diagnostics = new List<Diagnostic> { diagnostic };
        var fails = new List<AnalysisFail>();

        await JsonEmitter.WriteAsync(diagnostics, fails, rules, stream, CancellationToken.None);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        var doc = JsonDocument.Parse(json);
        var rootElement = doc.RootElement;

        var diagnosticsArray = rootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.Single(diagnosticsArray);

        var d = diagnosticsArray[0];
        Assert.Equal("V3001", d.GetProperty("id").GetString());
        Assert.Equal("Warning", d.GetProperty("severity").GetString());
        Assert.Equal("Message", d.GetProperty("message").GetString());

        var loc = d.GetProperty("location");
        Assert.Equal("src/Foo.cs", loc.GetProperty("path").GetString());
        Assert.Equal(1, loc.GetProperty("line").GetInt32());
        Assert.Equal(7, loc.GetProperty("column").GetInt32());
    }

    [Fact]
    public async Task WriteAsyncWithOneDiagnosticWithoutLocation()
    {
        using var stream = new MemoryStream();
        var rules = new List<RuleDescriptor>();

        var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor("V3002", "Title", "No location message", "Category", DiagnosticSeverity.Error, true),
            Location.None);

        var diagnostics = new List<Diagnostic> { diagnostic };
        var fails = new List<AnalysisFail>();

        await JsonEmitter.WriteAsync(diagnostics, fails, rules, stream, CancellationToken.None);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        var doc = JsonDocument.Parse(json);
        var rootElement = doc.RootElement;

        var diagnosticsArray = rootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.Single(diagnosticsArray);

        var d = diagnosticsArray[0];
        Assert.Equal("V3002", d.GetProperty("id").GetString());
        Assert.Equal("Error", d.GetProperty("severity").GetString());
        Assert.Equal("No location message", d.GetProperty("message").GetString());
        Assert.True(d.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task WriteAsyncWithMultipleDiagnosticsAndFails()
    {
        using var stream = new MemoryStream();
        var rules = new List<RuleDescriptor>
        {
            new("V3001", RuleSeverity.Level1, "CWE-571", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast, typeof(DummyRule))
        };

        var source = "class C { void M() {} }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "src/Foo.cs");
        var root = await tree.GetRootAsync();
        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
        var location = classDecl.Identifier.GetLocation();

        var diagnostic1 = Diagnostic.Create(
            new DiagnosticDescriptor("V3001", "Title", "Message1", "Category", DiagnosticSeverity.Warning, true),
            location);

        var diagnostic2 = Diagnostic.Create(
            new DiagnosticDescriptor("V3002", "Title", "Message2", "Category", DiagnosticSeverity.Error, true),
            Location.None);

        var diagnostics = new List<Diagnostic> { diagnostic1, diagnostic2 };
        var fails = new List<AnalysisFail>
        {
            new("F001", "Fail message", "src/Bar.cs", 5)
        };

        await JsonEmitter.WriteAsync(diagnostics, fails, rules, stream, CancellationToken.None);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        var doc = JsonDocument.Parse(json);
        var rootElement = doc.RootElement;

        var diagnosticsArray = rootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.Equal(2, diagnosticsArray.Count);

        var failsArray = rootElement.GetProperty("fails").EnumerateArray().ToList();
        Assert.Single(failsArray);

        var f = failsArray[0];
        Assert.Equal("F001", f.GetProperty("code").GetString());
        Assert.Equal("Fail message", f.GetProperty("message").GetString());
        Assert.Equal("src/Bar.cs", f.GetProperty("filePath").GetString());
        Assert.Equal(5, f.GetProperty("line").GetInt32());
    }

    private static class DummyRule { }
}
