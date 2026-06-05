using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class TrackedKeyEqualityTests
{
    [Fact]
    public void Symbol_EqualByUnderlyingSymbol_UsingSymbolEqualityComparer()
    {
        var (_, model, _) = CfgTestHarness.Compile(@"
class C
{
    void M()
    {
        int x = 0;
        x = 1;
    }
}");
        var local = model.Compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<VariableDeclaratorSyntax>().First();
        var symbol = model.GetDeclaredSymbol(local)!;

        var k1 = new TrackedKey.Symbol(symbol);
        var k2 = new TrackedKey.Symbol(symbol);

        Assert.Equal(k1, k2);
        Assert.Equal(k1.GetHashCode(), k2.GetHashCode());
    }

    [Fact]
    public void Symbol_NotEqualToInstanceField()
    {
        var (_, model, _) = CfgTestHarness.Compile(@"
class C
{
    int f;
    void M()
    {
        int x = 0;
    }
}");
        var local = model.Compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<VariableDeclaratorSyntax>().First();
        var localSymbol = (ISymbol)model.GetDeclaredSymbol(local)!;
        var field = (IFieldSymbol)model.Compilation.GetTypeByMetadataName("C")!.GetMembers("f").First();

        TrackedKey k1 = new TrackedKey.Symbol(localSymbol);
        TrackedKey k2 = new TrackedKey.InstanceField(field);

        Assert.NotEqual(k1, k2);
    }
}
