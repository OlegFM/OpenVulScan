using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests;

public class NullStateTransferTests
{
    private static readonly NullStateTransfer _transfer = new();

    private static IOperation CompileExpression(string code, string setup = "")
    {
        var tree = CSharpSyntaxTree.ParseText($"class C {{ void M() {{ {setup} var __result = {code}; }} }}");
        var compilation = CSharpCompilation.Create(
            "test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var decl = method.DescendantNodes().OfType<VariableDeclarationSyntax>().Last();
        var init = decl.Variables[0].Initializer!.Value;
        return model.GetOperation(init)!;
    }

    [Fact]
    public void Apply_NullLiteral_ReturnsDefinitelyNull()
    {
        var op = CompileExpression("null");
        Assert.Equal(NullState.DefinitelyNull, _transfer.Apply(NullState.Unknown, op));
    }

    [Fact]
    public void Apply_Assignment_ReturnsRhsState()
    {
        var op = CompileExpression("x = y");
        Assert.Equal(NullState.NotNull, _transfer.Apply(NullState.NotNull, op));
        Assert.Equal(NullState.DefinitelyNull, _transfer.Apply(NullState.DefinitelyNull, op));
        Assert.Equal(NullState.MaybeNull, _transfer.Apply(NullState.MaybeNull, op));
    }

    [Fact]
    public void Apply_MemberAccess_DefinitelyNullReceiver_ReturnsBottom()
    {
        var op = CompileExpression("s.Length", "string s = \"\";");
        Assert.Equal(NullState.Unknown, _transfer.Apply(NullState.DefinitelyNull, op));
    }

    [Fact]
    public void Apply_MemberAccess_MaybeNullReceiver_ReturnsMaybeNull()
    {
        var op = CompileExpression("s.Length", "string s = \"\";");
        Assert.Equal(NullState.MaybeNull, _transfer.Apply(NullState.MaybeNull, op));
    }

    [Fact]
    public void Apply_MemberAccess_NotNullReceiver_ReturnsUnknown()
    {
        var op = CompileExpression("s.Length", "string s = \"\";");
        Assert.Equal(NullState.Unknown, _transfer.Apply(NullState.NotNull, op));
    }

    [Fact]
    public void Apply_ConditionalAccess_DefinitelyNullReceiver_ReturnsDefinitelyNull()
    {
        var op = CompileExpression("s?.Length", "string? s = null;");
        Assert.Equal(NullState.DefinitelyNull, _transfer.Apply(NullState.DefinitelyNull, op));
    }

    [Fact]
    public void Apply_ConditionalAccess_MaybeNullReceiver_ReturnsMaybeNull()
    {
        var op = CompileExpression("s?.Length", "string? s = null;");
        Assert.Equal(NullState.MaybeNull, _transfer.Apply(NullState.MaybeNull, op));
    }

    [Fact]
    public void Apply_ConditionalAccess_NotNullReceiver_ReturnsUnknown()
    {
        var op = CompileExpression("s?.Length", "string? s = null;");
        Assert.Equal(NullState.Unknown, _transfer.Apply(NullState.NotNull, op));
    }

    [Fact]
    public void Apply_Coalesce_NotNullState_ReturnsNotNull()
    {
        var op = CompileExpression("x ?? y");
        Assert.Equal(NullState.NotNull, _transfer.Apply(NullState.NotNull, op));
    }

    [Fact]
    public void Apply_Coalesce_DefinitelyNullState_ReturnsDefinitelyNull()
    {
        var op = CompileExpression("x ?? y");
        Assert.Equal(NullState.DefinitelyNull, _transfer.Apply(NullState.DefinitelyNull, op));
    }

    [Fact]
    public void Apply_Coalesce_MaybeNullState_ReturnsMaybeNull()
    {
        var op = CompileExpression("x ?? y");
        Assert.Equal(NullState.MaybeNull, _transfer.Apply(NullState.MaybeNull, op));
    }

    [Fact]
    public void Apply_Coalesce_UnknownState_ReturnsUnknown()
    {
        var op = CompileExpression("x ?? y");
        Assert.Equal(NullState.Unknown, _transfer.Apply(NullState.Unknown, op));
    }

    [Fact]
    public void RefineForNullCheck_DefinitelyNull_StaysDefinitelyNull()
    {
        Assert.Equal(NullState.DefinitelyNull, NullStateTransfer.RefineForNullCheck(NullState.DefinitelyNull));
    }

    [Fact]
    public void RefineForNullCheck_NotNull_ReturnsBottom()
    {
        Assert.Equal(NullState.Unknown, NullStateTransfer.RefineForNullCheck(NullState.NotNull));
    }

    [Fact]
    public void RefineForNullCheck_MaybeNull_BecomesDefinitelyNull()
    {
        Assert.Equal(NullState.DefinitelyNull, NullStateTransfer.RefineForNullCheck(NullState.MaybeNull));
    }

    [Fact]
    public void RefineForNullCheck_Unknown_BecomesDefinitelyNull()
    {
        Assert.Equal(NullState.DefinitelyNull, NullStateTransfer.RefineForNullCheck(NullState.Unknown));
    }

    [Fact]
    public void RefineForNotNullCheck_DefinitelyNull_ReturnsBottom()
    {
        Assert.Equal(NullState.Unknown, NullStateTransfer.RefineForNotNullCheck(NullState.DefinitelyNull));
    }

    [Fact]
    public void RefineForNotNullCheck_NotNull_StaysNotNull()
    {
        Assert.Equal(NullState.NotNull, NullStateTransfer.RefineForNotNullCheck(NullState.NotNull));
    }

    [Fact]
    public void RefineForNotNullCheck_MaybeNull_BecomesNotNull()
    {
        Assert.Equal(NullState.NotNull, NullStateTransfer.RefineForNotNullCheck(NullState.MaybeNull));
    }

    [Fact]
    public void RefineForNotNullCheck_Unknown_BecomesNotNull()
    {
        Assert.Equal(NullState.NotNull, NullStateTransfer.RefineForNotNullCheck(NullState.Unknown));
    }

    [Fact]
    public void Apply_NullableValueType_HasValue_ReturnsNotNull()
    {
        var op = CompileExpression("n.Value", "int? n = 1;");
        Assert.Equal(NullState.NotNull, _transfer.Apply(NullState.NotNull, op));
    }

    [Fact]
    public void Apply_NullableValueType_HasValue_OnMaybeNull_ReturnsNotNull()
    {
        var op = CompileExpression("n.Value", "int? n = 1;");
        Assert.Equal(NullState.NotNull, _transfer.Apply(NullState.MaybeNull, op));
    }

    [Fact(Skip = "Generic constraint initial states are determined by the solver, not the transfer function. Transfer tests operate on a single aggregated NullState and cannot meaningfully test generic constraint handling without solver integration.")]
    public void Apply_GenericWithClassConstraint_KnownNotNull_ReturnsNotNull()
    {
        // Placeholder for solver-level integration test.
    }

    [Fact]
    public void Apply_BasicBlock_ReturnsInputState()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { } }");
        var compilation = CSharpCompilation.Create(
            "test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var cfg = Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph.Create(
            method, model, System.Threading.CancellationToken.None)!;
        var block = cfg.Blocks[0];

        Assert.Equal(NullState.NotNull, _transfer.Apply(NullState.NotNull, block));
        Assert.Equal(NullState.DefinitelyNull, _transfer.Apply(NullState.DefinitelyNull, block));
    }
}
