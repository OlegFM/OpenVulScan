#pragma warning disable CA1812 // Avoid uninstantiated internal classes

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests;

public class SarifWriterTests
{
    [Fact]
    public void WriteProducesExpectedSarifJson()
    {
        // Arrange
        var writer = new SarifWriter("OpenVulScan", "1.0.0");

        var rules = new List<RuleDescriptor>
        {
            new("V3001", RuleSeverity.Level1, "CWE-571", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast, typeof(DummyRule1)),
            new("V3002", RuleSeverity.Level2, "CWE-89", RuleCategory.Owasp, AnalysisCapability.DataFlow, typeof(DummyRule2))
        };

        var source = "class C { void M() {} }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "src/Foo.cs");
        var root = tree.GetRoot();
        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
        var location = classDecl.Identifier.GetLocation();

        var diagnostic1 = Diagnostic.Create(
            new DiagnosticDescriptor("V3001", "Title1", "Message1", "Category", DiagnosticSeverity.Warning, true),
            location);

        var diagnostic2 = Diagnostic.Create(
            new DiagnosticDescriptor("V3002", "Title2", "Message2", "Category", DiagnosticSeverity.Error, true),
            Location.None);

        var diagnostics = new List<Diagnostic> { diagnostic1, diagnostic2 };

        using var stream = new MemoryStream();

        // Act
        writer.Write(diagnostics, new List<AnalysisFail>(), rules, stream);

        // Assert
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        // Write sample file for manual validation
        var samplePath = Path.Combine(AppContext.BaseDirectory, "sample.sarif");
        File.WriteAllText(samplePath, json);

        // Structural assertions
        var doc = JsonDocument.Parse(json);
        var rootElement = doc.RootElement;

        Assert.Equal("https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json", rootElement.GetProperty("$schema").GetString());
        Assert.Equal("2.1.0", rootElement.GetProperty("version").GetString());

        var runs = rootElement.GetProperty("runs").EnumerateArray().ToList();
        Assert.Single(runs);

        var run = runs[0];
        var driver = run.GetProperty("tool").GetProperty("driver");
        Assert.Equal("OpenVulScan", driver.GetProperty("name").GetString());
        Assert.Equal("1.0.0", driver.GetProperty("version").GetString());

        var rulesArray = driver.GetProperty("rules").EnumerateArray().ToList();
        Assert.Equal(2, rulesArray.Count);

        var rule1 = rulesArray[0];
        Assert.Equal("V3001", rule1.GetProperty("id").GetString());
        Assert.Equal("DummyRule1", rule1.GetProperty("name").GetString());
        Assert.Equal("DummyRule1", rule1.GetProperty("shortDescription").GetProperty("text").GetString());
        Assert.Equal("CWE-571", rule1.GetProperty("properties").GetProperty("cwe").GetString());
        Assert.Equal("GeneralAnalysis", rule1.GetProperty("properties").GetProperty("category").GetString());

        var rule2 = rulesArray[1];
        Assert.Equal("V3002", rule2.GetProperty("id").GetString());
        Assert.Equal("DummyRule2", rule2.GetProperty("name").GetString());
        Assert.Equal("CWE-89", rule2.GetProperty("properties").GetProperty("cwe").GetString());
        Assert.Equal("Owasp", rule2.GetProperty("properties").GetProperty("category").GetString());

        var results = run.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(2, results.Count);

        var result1 = results[0];
        Assert.Equal("V3001", result1.GetProperty("ruleId").GetString());
        Assert.Equal("warning", result1.GetProperty("level").GetString());
        Assert.Equal("Message1", result1.GetProperty("message").GetProperty("text").GetString());

        var locations1 = result1.GetProperty("locations").EnumerateArray().ToList();
        Assert.Single(locations1);
        var physicalLoc1 = locations1[0].GetProperty("physicalLocation");
        Assert.Equal("src/Foo.cs", physicalLoc1.GetProperty("artifactLocation").GetProperty("uri").GetString());
        var region1 = physicalLoc1.GetProperty("region");
        Assert.Equal(1, region1.GetProperty("startLine").GetInt32());
        Assert.Equal(7, region1.GetProperty("startColumn").GetInt32());
        Assert.Equal(1, region1.GetProperty("endLine").GetInt32());
        Assert.Equal(8, region1.GetProperty("endColumn").GetInt32());

        var result2 = results[1];
        Assert.Equal("V3002", result2.GetProperty("ruleId").GetString());
        Assert.Equal("error", result2.GetProperty("level").GetString());
        Assert.Equal("Message2", result2.GetProperty("message").GetProperty("text").GetString());
        Assert.False(result2.TryGetProperty("locations", out _));

        // Snapshot comparison omitted — structural assertions above verify correctness
    }

    private sealed class DummyRule1 { }

    private sealed class DummyRule2 { }
}
