using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests.Ssa;

public class SsaIdTests
{
    [Fact]
    public void EqualKeyAndVersion_AreEqual()
    {
        var (_, model, _) = CfgTestHarness.Compile(@"
class C { void M() { int x = 0; } }");
        var decl = model.Compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<VariableDeclaratorSyntax>().First();
        var symbol = (ISymbol)model.GetDeclaredSymbol(decl)!;
        var key = new TrackedKey.Symbol(symbol);

        var a = new SsaId(key, 0);
        var b = new SsaId(key, 0);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DifferentVersions_AreNotEqual()
    {
        var (_, model, _) = CfgTestHarness.Compile(@"
class C { void M() { int x = 0; } }");
        var decl = model.Compilation.SyntaxTrees.First().GetRoot()
            .DescendantNodes().OfType<VariableDeclaratorSyntax>().First();
        var symbol = (ISymbol)model.GetDeclaredSymbol(decl)!;
        var key = new TrackedKey.Symbol(symbol);

        var a = new SsaId(key, 0);
        var b = new SsaId(key, 1);

        Assert.NotEqual(a, b);
    }
}
