using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace OpenVulScan;

/// <summary>
/// V3151 — a variable is used as a divisor and only afterwards compared to zero, which
/// reveals the author believed it could be zero (a potential division by zero).
/// </summary>
/// <remarks>
/// <para>
/// Intra-block analysis: within a single basic block operation order is total and there
/// are no back-edges, so a divisor seen before a same-version zero-check is a genuine
/// divide-before-check. The canonical pattern (<c>int r = a / b; if (b == 0) …</c>) lives
/// in one block because plain statements do not split a block — only branches do.
/// </para>
/// <para>
/// Cross-block correlation is intentionally out of scope: a may-style data-flow over the
/// whole method would flag loop guards (<c>if (b == 0) break; … a / b;</c>) as false
/// positives via the loop back-edge. Precision is preferred over completeness here; the
/// dominance-based cross-block extension is a follow-up.
/// </para>
/// <para>
/// Like V3142 it is an <see cref="AstRule"/> (a per-method hook) that builds its own CFG
/// and SSA index; SSA-version identity is what distinguishes a re-assigned divisor from
/// the value actually divided by.
/// </para>
/// </remarks>
[Rule("V3151", RuleSeverity.Level2, "CWE-369", RuleCategory.GeneralAnalysis, AnalysisCapability.DataFlow)]
public sealed class V3151DivisionBeforeZeroCheck : AstRule
{
    private static readonly DiagnosticDescriptor s_descriptor = new(
        "V3151",
        "Division before zero check",
        "Variable was used as a divisor before being compared to zero",
        "GeneralAnalysis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override void OnMethodDeclaration(SyntaxNodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var cancellationToken = context.CancellationToken;
        var operation = context.SemanticModel.GetOperation(context.Node, cancellationToken);
        if (operation is not IMethodBodyOperation methodBody)
        {
            return;
        }

        var cfg = ControlFlowGraph.Create(methodBody, cancellationToken);
        var ssa = SsaBuilder.Build(cfg, context.SemanticModel);

        foreach (var block in cfg.Blocks)
        {
            var divided = new HashSet<SsaId>();
            foreach (var op in OperationTree.Enumerate(block))
            {
                if (TryGetDivisorVersion(op, ssa) is { } divisorVersion)
                {
                    divided.Add(divisorVersion);
                }
                else if (TryGetZeroCheckVersion(op, ssa) is { } checkVersion && divided.Contains(checkVersion))
                {
                    context.ReportDiagnostic(Diagnostic.Create(s_descriptor, op.Syntax.GetLocation()));
                }
            }
        }
    }

    private static SsaId? TryGetDivisorVersion(IOperation op, SsaIndex ssa) => op switch
    {
        IBinaryOperation { OperatorKind: BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder } bin =>
            ResolveVersion(bin.RightOperand, ssa),
        ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder } comp =>
            ResolveVersion(comp.Value, ssa),
        _ => null,
    };

    private static SsaId? TryGetZeroCheckVersion(IOperation op, SsaIndex ssa)
    {
        if (op is not IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } bin)
        {
            return null;
        }

        if (IsZeroLiteral(bin.RightOperand))
        {
            return ResolveVersion(bin.LeftOperand, ssa);
        }

        if (IsZeroLiteral(bin.LeftOperand))
        {
            return ResolveVersion(bin.RightOperand, ssa);
        }

        return null;
    }

    private static SsaId? ResolveVersion(IOperation operand, SsaIndex ssa)
    {
        operand = Unwrap(operand);
        return operand switch
        {
            ILocalReferenceOperation l => ssa.UseAt(l, new TrackedKey.Symbol(l.Local)),
            IParameterReferenceOperation p => ssa.UseAt(p, new TrackedKey.Symbol(p.Parameter)),
            IFieldReferenceOperation { Instance: IInstanceReferenceOperation } f =>
                ssa.UseAt(f, new TrackedKey.InstanceField(f.Field)),
            _ => null,
        };
    }

    private static bool IsZeroLiteral(IOperation op)
    {
        op = Unwrap(op);
        return op is ILiteralOperation { ConstantValue: { HasValue: true, Value: { } value } } && IsNumericZero(value);
    }

    private static bool IsNumericZero(object value) => value switch
    {
        int v => v == 0,
        long v => v == 0,
        short v => v == 0,
        sbyte v => v == 0,
        byte v => v == 0,
        ushort v => v == 0,
        uint v => v == 0,
        ulong v => v == 0,
        double v => v == 0,
        float v => v == 0,
        decimal v => v == 0,
        _ => false,
    };

    private static IOperation Unwrap(IOperation op) => op switch
    {
        IConversionOperation c => Unwrap(c.Operand),
        IParenthesizedOperation p => Unwrap(p.Operand),
        _ => op,
    };
}
