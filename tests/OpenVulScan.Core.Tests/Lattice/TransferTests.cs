using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Xunit;

namespace OpenVulScan.Tests;

public class TransferTests
{
    [Fact]
    public void ITransfer_HasBothApplySignatures()
    {
        var type = typeof(ITransfer<BoolLatticeValue>);
        var methods = type.GetMethods();

        var operationApply = Assert.Single(methods, m =>
            m.Name == "Apply" &&
            m.GetParameters().Length == 2 &&
            m.GetParameters()[1].ParameterType == typeof(IOperation));

        var blockApply = Assert.Single(methods, m =>
            m.Name == "Apply" &&
            m.GetParameters().Length == 2 &&
            m.GetParameters()[1].ParameterType == typeof(BasicBlock));

        Assert.NotNull(operationApply);
        Assert.NotNull(blockApply);
    }

    [Fact]
    public void TransferStub_ImplementsBothApplyMethods()
    {
        var transfer = new StubTransfer();
        var state = BoolLatticeValue.Bottom;

        var operationResult = transfer.Apply(state, (IOperation)null!);
        Assert.Equal(BoolLatticeValue.True, operationResult);

        var blockResult = transfer.Apply(state, (BasicBlock)null!);
        Assert.Equal(BoolLatticeValue.False, blockResult);
    }

    private sealed class StubTransfer : ITransfer<BoolLatticeValue>
    {
        public BoolLatticeValue Apply(BoolLatticeValue state, IOperation operation)
            => BoolLatticeValue.True;

        public BoolLatticeValue Apply(BoolLatticeValue state, BasicBlock block)
            => BoolLatticeValue.False;
    }
}
