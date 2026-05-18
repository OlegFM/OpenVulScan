using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests;

public class InlineSuppressionParserTests
{
    [Fact]
    public void DisableCurrentLineWithSpecificRuleReturnsRangeForCurrentLine()
    {
        var source = "// ovs:disable V3001\nclass C { }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "test.cs");

        var ranges = InlineSuppressionParser.Parse(tree);

        Assert.Single(ranges);
        Assert.Equal("test.cs", ranges[0].FilePath);
        Assert.Equal(0, ranges[0].StartLine);
        Assert.Equal(0, ranges[0].EndLine);
        Assert.Single(ranges[0].RuleCodes);
        Assert.Contains("V3001", ranges[0].RuleCodes);
    }

    [Fact]
    public void DisableNextLineWithSpecificRuleReturnsRangeForNextLine()
    {
        var source = "// ovs:disable-next-line V3001\nclass C { }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "test.cs");

        var ranges = InlineSuppressionParser.Parse(tree);

        Assert.Single(ranges);
        Assert.Equal("test.cs", ranges[0].FilePath);
        Assert.Equal(1, ranges[0].StartLine);
        Assert.Equal(1, ranges[0].EndLine);
        Assert.Single(ranges[0].RuleCodes);
        Assert.Contains("V3001", ranges[0].RuleCodes);
    }

    [Fact]
    public void DisableBlockEnableBlockWithSpecificRuleReturnsRangeBetweenMarkers()
    {
        var source = "// ovs:disable-block V3001\nclass C { }\n// ovs:enable-block V3001";
        var tree = CSharpSyntaxTree.ParseText(source, path: "test.cs");

        var ranges = InlineSuppressionParser.Parse(tree);

        Assert.Single(ranges);
        Assert.Equal("test.cs", ranges[0].FilePath);
        Assert.Equal(0, ranges[0].StartLine);
        Assert.Equal(2, ranges[0].EndLine);
        Assert.Single(ranges[0].RuleCodes);
        Assert.Contains("V3001", ranges[0].RuleCodes);
    }

    [Fact]
    public void DisableBlockWithoutEnableBlockReturnsRangeToEndOfFile()
    {
        var source = "// ovs:disable-block V3001\nclass C { }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "test.cs");

        var ranges = InlineSuppressionParser.Parse(tree);

        Assert.Single(ranges);
        Assert.Equal("test.cs", ranges[0].FilePath);
        Assert.Equal(0, ranges[0].StartLine);
        Assert.Equal(1, ranges[0].EndLine);
        Assert.Single(ranges[0].RuleCodes);
        Assert.Contains("V3001", ranges[0].RuleCodes);
    }

    [Fact]
    public void EnableBlockWithoutDisableBlockIsIgnored()
    {
        var source = "// ovs:enable-block V3001\nclass C { }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "test.cs");

        var ranges = InlineSuppressionParser.Parse(tree);

        Assert.Empty(ranges);
    }

    [Fact]
    public void MultipleRuleCodesParsedCorrectly()
    {
        var source = "// ovs:disable V3001,V3002\nclass C { }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "test.cs");

        var ranges = InlineSuppressionParser.Parse(tree);

        Assert.Single(ranges);
        Assert.Equal(2, ranges[0].RuleCodes.Count);
        Assert.Contains("V3001", ranges[0].RuleCodes);
        Assert.Contains("V3002", ranges[0].RuleCodes);
    }

    [Fact]
    public void NoRuleCodesSuppressesAllRules()
    {
        var source = "// ovs:disable-next-line\nclass C { }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "test.cs");

        var ranges = InlineSuppressionParser.Parse(tree);

        Assert.Single(ranges);
        Assert.Empty(ranges[0].RuleCodes);
    }

    [Fact]
    public void MalformedMarkersDoNotThrow()
    {
        var source = "// ovs:unknown V3001\n// ovs:disable\nclass C { }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "test.cs");

        var ranges = InlineSuppressionParser.Parse(tree);

        Assert.Single(ranges);
        Assert.Equal(1, ranges[0].StartLine);
        Assert.Equal(1, ranges[0].EndLine);
        Assert.Empty(ranges[0].RuleCodes);
    }

    [Fact]
    public void DisableAndDisableNextLineCombinedCorrectly()
    {
        var source = "// ovs:disable V3001\n// ovs:disable-next-line V3002\nclass C { }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "test.cs");

        var ranges = InlineSuppressionParser.Parse(tree);

        Assert.Equal(2, ranges.Count);
    }
}
