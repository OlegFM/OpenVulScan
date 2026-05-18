using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests;

public class SuppressMessageAttributeParserTests
{
    private static CSharpCompilation CreateCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "test.cs");
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Diagnostics.CodeAnalysis.SuppressMessageAttribute).Assembly.Location)
        };
        return CSharpCompilation.Create("TestAssembly", new[] { tree }, references);
    }

    [Fact]
    public void ClassLevelSuppressionIsParsed()
    {
        var source =
            "using System.Diagnostics.CodeAnalysis;\n" +
            "[SuppressMessage(\"OpenVulScan\", \"V3001\")]\n" +
            "class C { }";
        var compilation = CreateCompilation(source);

        var ranges = SuppressMessageAttributeParser.Parse(compilation);

        Assert.Single(ranges);
        Assert.Equal("test.cs", ranges[0].FilePath);
        Assert.Contains("V3001", ranges[0].RuleCodes);
        Assert.Equal(1, ranges[0].StartLine);
        Assert.Equal(2, ranges[0].EndLine);
    }

    [Fact]
    public void MethodLevelSuppressionIsParsed()
    {
        var source =
            "using System.Diagnostics.CodeAnalysis;\n" +
            "class C\n" +
            "{\n" +
            "    [SuppressMessage(\"OpenVulScan\", \"V3001\")]\n" +
            "    void M() { }\n" +
            "}";
        var compilation = CreateCompilation(source);

        var ranges = SuppressMessageAttributeParser.Parse(compilation);

        Assert.Single(ranges);
        Assert.Equal("test.cs", ranges[0].FilePath);
        Assert.Contains("V3001", ranges[0].RuleCodes);
    }

    [Fact]
    public void PropertyLevelSuppressionIsParsed()
    {
        var source =
            "using System.Diagnostics.CodeAnalysis;\n" +
            "class C\n" +
            "{\n" +
            "    [SuppressMessage(\"OpenVulScan\", \"V3001\")]\n" +
            "    int P { get; set; }\n" +
            "}";
        var compilation = CreateCompilation(source);

        var ranges = SuppressMessageAttributeParser.Parse(compilation);

        Assert.Single(ranges);
        Assert.Contains("V3001", ranges[0].RuleCodes);
    }

    [Fact]
    public void WrongCategoryIsNotParsed()
    {
        var source =
            "using System.Diagnostics.CodeAnalysis;\n" +
            "[SuppressMessage(\"OtherTool\", \"V3001\")]\n" +
            "class C { }";
        var compilation = CreateCompilation(source);

        var ranges = SuppressMessageAttributeParser.Parse(compilation);

        Assert.Empty(ranges);
    }

    [Fact]
    public void AllRulesSuppressionWithEmptyCheckId()
    {
        var source =
            "using System.Diagnostics.CodeAnalysis;\n" +
            "[SuppressMessage(\"OpenVulScan\", \"\")]\n" +
            "class C { }";
        var compilation = CreateCompilation(source);

        var ranges = SuppressMessageAttributeParser.Parse(compilation);

        Assert.Single(ranges);
        Assert.Empty(ranges[0].RuleCodes);
    }

    [Fact]
    public void MultipleRuleCodesAreParsed()
    {
        var source =
            "using System.Diagnostics.CodeAnalysis;\n" +
            "[SuppressMessage(\"OpenVulScan\", \"V3001,V3002\")]\n" +
            "class C { }";
        var compilation = CreateCompilation(source);

        var ranges = SuppressMessageAttributeParser.Parse(compilation);

        Assert.Single(ranges);
        Assert.Equal(2, ranges[0].RuleCodes.Count);
        Assert.Contains("V3001", ranges[0].RuleCodes);
        Assert.Contains("V3002", ranges[0].RuleCodes);
    }

    [Fact]
    public void AttributeSuffixIsRecognized()
    {
        var source =
            "using System.Diagnostics.CodeAnalysis;\n" +
            "[SuppressMessageAttribute(\"OpenVulScan\", \"V3001\")]\n" +
            "class C { }";
        var compilation = CreateCompilation(source);

        var ranges = SuppressMessageAttributeParser.Parse(compilation);

        Assert.Single(ranges);
        Assert.Contains("V3001", ranges[0].RuleCodes);
    }

    [Fact]
    public void WithoutUsingFullyQualifiedNameWorks()
    {
        var source =
            "[System.Diagnostics.CodeAnalysis.SuppressMessage(\"OpenVulScan\", \"V3001\")]\n" +
            "class C { }";
        var compilation = CreateCompilation(source);

        var ranges = SuppressMessageAttributeParser.Parse(compilation);

        Assert.Single(ranges);
        Assert.Contains("V3001", ranges[0].RuleCodes);
    }
}
