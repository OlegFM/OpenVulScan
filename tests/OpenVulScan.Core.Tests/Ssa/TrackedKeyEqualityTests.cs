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
        var root = model.Compilation.SyntaxTrees.First().GetRoot();

        // Resolve the symbol via the declaration site.
        var declarator = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .First(v => v.Identifier.Text == "x");
        var symbolFromDecl = (ISymbol)model.GetDeclaredSymbol(declarator)!;

        // Resolve the same symbol via a reference site (the assignment target).
        var refNode = root.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .First(n => n.Identifier.Text == "x"
                && n.Parent is AssignmentExpressionSyntax);
        var symbolFromRef = model.GetSymbolInfo(refNode).Symbol!;

        var k1 = new TrackedKey.Symbol(symbolFromDecl);
        var k2 = new TrackedKey.Symbol(symbolFromRef);

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
        var root = model.Compilation.SyntaxTrees.First().GetRoot();

        // Pick the local 'x' by name so we don't accidentally grab the field declarator 'f'.
        var localDeclarator = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .First(v => v.Identifier.Text == "x");
        var localSymbol = (ILocalSymbol)model.GetDeclaredSymbol(localDeclarator)!;
        var field = (IFieldSymbol)model.Compilation.GetTypeByMetadataName("C")!.GetMembers("f").First();

        TrackedKey k1 = new TrackedKey.Symbol((ISymbol)localSymbol);
        TrackedKey k2 = new TrackedKey.InstanceField(field);

        Assert.NotEqual(k1, k2);
    }
}
