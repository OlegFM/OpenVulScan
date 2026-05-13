using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: Rule3001 <path-to-cs-file>");
    Console.WriteLine("Options:");
    Console.WriteLine("  -h, --help    Show this help message");
    return args.Length == 0 ? 1 : 0;
}

string filePath = args[0];
if (!File.Exists(filePath))
{
    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "File not found: {0}", filePath));
    return 1;
}

string source = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
var tree = CSharpSyntaxTree.ParseText(source, path: filePath);

var compilation = CSharpCompilation.Create(
    "Rule3001Compilation",
    new[] { tree },
    new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

var semanticModel = compilation.GetSemanticModel(tree);
var diagnostics = compilation.GetDiagnostics();

var fatalDiagnostics = diagnostics
    .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS5001")
    .ToList();

if (fatalDiagnostics.Count != 0)
{
    Console.WriteLine("Compilation errors:");
    foreach (var d in fatalDiagnostics)
    {
        Console.WriteLine(d.ToString());
    }
    return 1;
}

var root = await tree.GetRootAsync().ConfigureAwait(false);
var methods = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().ToList();

int hits = 0;

foreach (var method in methods)
{
    var operation = semanticModel.GetOperation(method);
    if (operation is null)
        continue;

    var walker = new BinaryOperationWalker();
    walker.Walk(operation);

    foreach (var hit in walker.Hits)
    {
        var lineSpan = hit.Syntax.SyntaxTree.GetLineSpan(hit.Syntax.Span);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0}:{1}: V3001 [{2}] – left and right operands are identical",
            filePath,
            lineSpan.StartLinePosition.Line + 1,
            hit.OperatorKind));
        hits++;
    }
}

Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Total hits: {0}", hits));
return 0;

sealed class BinaryOperationWalker : OperationVisitor
{
    public List<IBinaryOperation> Hits { get; } = new();

    public void Walk(IOperation? operation)
    {
        if (operation is null)
            return;

        operation.Accept(this);
    }

    public override void VisitBinaryOperator(IBinaryOperation operation)
    {
        if (IsCheckedOperator(operation.OperatorKind))
        {
            if (AreStructurallyEqual(operation.LeftOperand, operation.RightOperand))
            {
                Hits.Add(operation);
            }
        }

        base.VisitBinaryOperator(operation);
    }

    public override void DefaultVisit(IOperation operation)
    {
        foreach (var child in operation.ChildOperations)
        {
            Walk(child);
        }
    }

    private static bool IsCheckedOperator(BinaryOperatorKind kind)
    {
        return kind switch
        {
            BinaryOperatorKind.Equals => true,
            BinaryOperatorKind.NotEquals => true,
            BinaryOperatorKind.Add => true,
            BinaryOperatorKind.Subtract => true,
            BinaryOperatorKind.Multiply => true,
            BinaryOperatorKind.Divide => true,
            BinaryOperatorKind.Remainder => true,
            BinaryOperatorKind.ConditionalAnd => true,
            BinaryOperatorKind.ConditionalOr => true,
            BinaryOperatorKind.And => true,
            BinaryOperatorKind.Or => true,
            BinaryOperatorKind.ExclusiveOr => true,
            _ => false,
        };
    }

    private static bool AreStructurallyEqual(IOperation? a, IOperation? b)
    {
        if (a is null || b is null)
            return a is null && b is null;

        // Unwrap conversions and parentheses
        a = Unwrap(a);
        b = Unwrap(b);

        if (a.Kind != b.Kind)
            return false;

        switch (a)
        {
            case IBinaryOperation binA when b is IBinaryOperation binB:
                return binA.OperatorKind == binB.OperatorKind
                    && AreStructurallyEqual(binA.LeftOperand, binB.LeftOperand)
                    && AreStructurallyEqual(binA.RightOperand, binB.RightOperand);

            case IUnaryOperation unA when b is IUnaryOperation unB:
                return unA.OperatorKind == unB.OperatorKind
                    && AreStructurallyEqual(unA.Operand, unB.Operand);

            case ILiteralOperation litA when b is ILiteralOperation litB:
                return Equals(litA.ConstantValue.Value, litB.ConstantValue.Value);

            case ILocalReferenceOperation locA when b is ILocalReferenceOperation locB:
                return locA.Local.Name == locB.Local.Name;

            case IParameterReferenceOperation parA when b is IParameterReferenceOperation parB:
                return parA.Parameter.Name == parB.Parameter.Name;

            case IFieldReferenceOperation fldA when b is IFieldReferenceOperation fldB:
                return fldA.Field.Name == fldB.Field.Name
                    && AreStructurallyEqual(fldA.Instance, fldB.Instance);

            case IPropertyReferenceOperation propA when b is IPropertyReferenceOperation propB:
                return propA.Property.Name == propB.Property.Name
                    && AreStructurallyEqual(propA.Instance, propB.Instance);

            case IInstanceReferenceOperation instA when b is IInstanceReferenceOperation instB:
                return instA.ReferenceKind == instB.ReferenceKind;

            case IInvocationOperation invA when b is IInvocationOperation invB:
                return invA.TargetMethod.Name == invB.TargetMethod.Name
                    && AreStructurallyEqual(invA.Instance, invB.Instance)
                    && ArgumentsEqual(invA.Arguments, invB.Arguments);

            case IConditionalAccessOperation condA when b is IConditionalAccessOperation condB:
                return AreStructurallyEqual(condA.Operation, condB.Operation)
                    && AreStructurallyEqual(condA.WhenNotNull, condB.WhenNotNull);

            case IConditionalOperation ternA when b is IConditionalOperation ternB:
                return AreStructurallyEqual(ternA.Condition, ternB.Condition)
                    && AreStructurallyEqual(ternA.WhenTrue, ternB.WhenTrue)
                    && AreStructurallyEqual(ternA.WhenFalse, ternB.WhenFalse);

            default:
                // For unhandled operation types, fall back to syntax text comparison.
                // This keeps the spike pragmatic while remaining sound (conservative).
                return a.Syntax.ToString() == b.Syntax.ToString();
        }
    }

    private static IOperation Unwrap(IOperation op)
    {
        while (op is IConversionOperation conv)
        {
            op = conv.Operand;
        }

        while (op is IParenthesizedOperation paren)
        {
            op = paren.Operand;
        }

        return op;
    }

    private static bool ArgumentsEqual(IEnumerable<IArgumentOperation> aArgs, IEnumerable<IArgumentOperation> bArgs)
    {
        var aList = aArgs.ToList();
        var bList = bArgs.ToList();
        if (aList.Count != bList.Count)
            return false;

        for (int i = 0; i < aList.Count; i++)
        {
            if (!AreStructurallyEqual(aList[i].Value, bList[i].Value))
                return false;
        }

        return true;
    }
}
