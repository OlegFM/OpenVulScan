using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// A dereference of a possibly-null value, attributed to exactly one rule
/// code by provenance.
/// </summary>
internal sealed record NullDeref(string Code, string ReceiverName, Location Location);

/// <summary>
/// Shared deref-site detector and provenance classifier for the NRE rule
/// family. Returns at most one <see cref="NullDeref"/> per operation, so
/// one deref site never produces diagnostics from two family rules.
/// </summary>
internal static class NullDerefClassifier
{
    /// <summary>
    /// Classifies <paramref name="operation"/> if it is a deref site whose
    /// receiver is <see cref="NullState.DefinitelyNull"/> or
    /// <see cref="NullState.MaybeNull"/>; returns <see langword="null"/>
    /// otherwise. <see cref="NullState.Unknown"/> receivers are silent by
    /// design: no evidence, no warning.
    /// </summary>
    public static NullDeref? Classify(
        IOperation operation,
        ImmutableDictionary<SsaId, NullState> state,
        SsaIndex ssa)
    {
        var receiver = operation switch
        {
            IAwaitOperation awaitOp => awaitOp.Operation,
            IMemberReferenceOperation { Instance: { } instance } => instance,
            IInvocationOperation { Instance: { } instance } => instance,
            IArrayElementReferenceOperation arrayRef => arrayRef.ArrayReference,
            _ => null,
        };

        if (receiver is null)
        {
            return null;
        }

        receiver = Unwrap(receiver);

        TrackedKey? key = receiver switch
        {
            ILocalReferenceOperation l => new TrackedKey.Symbol(l.Local),
            IParameterReferenceOperation p => new TrackedKey.Symbol(p.Parameter),
            IFieldReferenceOperation { Instance: IInstanceReferenceOperation } f => new TrackedKey.InstanceField(f.Field),
            IFlowCaptureReferenceOperation c => new TrackedKey.Capture(c.Id),
            _ => null,
        };

        if (key is null || ssa.UseAt(receiver, key) is not { } id)
        {
            return null;
        }

        var nullState = state.TryGetValue(id, out var s) ? s : NullState.Unknown;
        if (nullState is not (NullState.DefinitelyNull or NullState.MaybeNull))
        {
            return null;
        }

        var code = ClassifyProvenance(operation, receiver, id, ssa);
        var name = receiver.Syntax.ToString();
        return new NullDeref(code, name, operation.Syntax.GetLocation());
    }

    private static string ClassifyProvenance(
        IOperation operation,
        IOperation receiver,
        SsaId id,
        SsaIndex ssa)
    {
        if (operation is IAwaitOperation)
        {
            return "V3168";
        }

        if (UnwrapParens(receiver.Syntax) is ConditionalAccessExpressionSyntax)
        {
            return "V3153";
        }

        if (receiver is ILocalReferenceOperation or IParameterReferenceOperation
            && DefIsConditionalAccess(id, ssa))
        {
            return "V3105";
        }

        return "V3080";
    }

    private static bool DefIsConditionalAccess(SsaId id, SsaIndex ssa)
    {
        // φ-results and entry versions have no def site — provenance is
        // ambiguous there and falls back to V3080.
        var rhsSyntax = ssa.DefSiteOf(id) switch
        {
            IVariableDeclaratorOperation { Initializer.Value: { } value } => value.Syntax,
            ISimpleAssignmentOperation assignment => assignment.Value.Syntax,
            _ => null,
        };

        return rhsSyntax is not null && UnwrapParens(rhsSyntax) is ConditionalAccessExpressionSyntax;
    }

    private static SyntaxNode UnwrapParens(SyntaxNode syntax)
    {
        while (syntax is ParenthesizedExpressionSyntax paren)
        {
            syntax = paren.Expression;
        }

        return syntax;
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conv:
                    operation = conv.Operand;
                    continue;
                case IParenthesizedOperation paren:
                    operation = paren.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }
}
