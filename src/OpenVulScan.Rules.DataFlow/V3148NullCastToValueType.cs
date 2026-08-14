using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// V3148 — casting a potential <see langword="null"/> value to a value type
/// (<c>(int)nullableX</c>, unboxing <c>(int)obj</c>) can throw at runtime.
/// </summary>
/// <remarks>
/// Part of the NRE family (shared NullState solve). Reports an explicit conversion to a
/// non-nullable value type whose operand is <see cref="NullState.DefinitelyNull"/> or
/// <see cref="NullState.MaybeNull"/> and is of <see cref="Nullable{T}"/> or a reference
/// type. Path-sensitive: <c>x != null</c> / <c>x.HasValue</c> guards clear the state via
/// the family edge refiner. Cross-variable correlation (PVS's min/max example) is out of
/// scope by ADR-004.
/// </remarks>
[Rule("V3148", RuleSeverity.Level2, "CWE-476", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3148NullCastToValueType : NullStateRuleBase
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3148",
        "Null cast to value type",
        "Casting potential 'null' value of '{0}' to a value type can lead to NullReferenceException",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnState(
        IOperation operation,
        ImmutableDictionary<SsaId, NullState> state,
        DataFlowContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        if (operation is not IConversionOperation conversion
            || conversion.Type is not { IsValueType: true } targetType
            || targetType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return;
        }

        var operand = Unwrap(conversion.Operand);
        if (operand.Type is not { } operandType
            || !(operandType.IsReferenceType
                 || operandType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T))
        {
            return;
        }

        var (nullState, name) = ResolveState(operand, state, context.SsaIndex);
        if (nullState is NullState.DefinitelyNull or NullState.MaybeNull)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_descriptor,
                conversion.Syntax.GetLocation(),
                name));
        }
    }

    private static (NullState State, string Name) ResolveState(
        IOperation operand,
        ImmutableDictionary<SsaId, NullState> state,
        SsaIndex ssa)
    {
        (TrackedKey Key, string Name)? tracked = operand switch
        {
            ILocalReferenceOperation l => (new TrackedKey.Symbol(l.Local), l.Local.Name),
            IParameterReferenceOperation p => (new TrackedKey.Symbol(p.Parameter), p.Parameter.Name),
            IFieldReferenceOperation { Instance: IInstanceReferenceOperation } f =>
                ((TrackedKey)new TrackedKey.InstanceField(f.Field), f.Field.Name),
            IFlowCaptureReferenceOperation c => ((TrackedKey)new TrackedKey.Capture(c.Id), operand.Syntax.ToString()),
            _ => null,
        };

        if (tracked is not { } t)
        {
            return (NullState.Unknown, operand.Syntax.ToString());
        }

        var use = ssa.UseAt(operand, t.Key);
        if (use is null || !state.TryGetValue(use.Value, out var s))
        {
            return (NullState.Unknown, t.Name);
        }

        return (s, t.Name);
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
