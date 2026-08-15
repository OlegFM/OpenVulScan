using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests;

public class NullabilitySummaryProviderTests
{
    [Fact]
    public void ReturnNullLiteral_IsDefinitelyNull()
    {
        Assert.Equal(NullState.DefinitelyNull, ReturnStateOf(@"
class C
{
    string F() { return null; }
}", "F"));
    }

    [Fact]
    public void ExpressionBodied_ReturnNull_IsDefinitelyNull()
    {
        Assert.Equal(NullState.DefinitelyNull, ReturnStateOf(@"
class C
{
    string F() => null;
}", "F"));
    }

    [Fact]
    public void BranchNullOrNew_IsMaybeNull()
    {
        Assert.Equal(NullState.MaybeNull, ReturnStateOf(@"
class C
{
    object F(bool b)
    {
        if (b)
        {
            return null;
        }

        return new object();
    }
}", "F"));
    }

    [Fact]
    public void ReturnNewObject_IsNotNull()
    {
        Assert.Equal(NullState.NotNull, ReturnStateOf(@"
class C
{
    object F() { return new object(); }
}", "F"));
    }

    [Fact]
    public void ChainThroughCallee_PropagatesDefinitelyNull()
    {
        Assert.Equal(NullState.DefinitelyNull, ReturnStateOf(@"
class C
{
    string F() { return null; }

    string G() { return F(); }
}", "G"));
    }

    [Fact]
    public void SelfRecursion_StabilizesToDefinitelyNull()
    {
        Assert.Equal(NullState.DefinitelyNull, ReturnStateOf(@"
class C
{
    string F(int n)
    {
        if (n > 0)
        {
            return F(n - 1);
        }

        return null;
    }
}", "F"));
    }

    [Fact]
    public void ValueTypeReturn_IsNotNull()
    {
        Assert.Equal(NullState.NotNull, ReturnStateOf(@"
class C
{
    int F() { return 1; }
}", "F"));
    }

    [Fact]
    public void ReturnParameter_IsUnknown()
    {
        Assert.Equal(NullState.Unknown, ReturnStateOf(@"
class C
{
    string F(string s) { return s; }
}", "F"));
    }

    private static NullState ReturnStateOf(string source, string methodName)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var lookup = NullabilitySummaryProvider.Compute(compilation, CancellationToken.None);

        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == methodName);
        var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        return lookup.ReturnStateOf(method);
    }
}
