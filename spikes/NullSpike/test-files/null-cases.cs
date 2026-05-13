public class NullCases
{
    public void NullLiteral()
    {
        string x = null;
    }

    public void NonNullLiteral()
    {
        string x = "hello";
    }

    public void VariableAssignment(string? input)
    {
        string x = input;
    }

    public void ConditionalAccess(string? input)
    {
        var x = input?.Length;
    }

    public void BranchingJoin(bool flag)
    {
        string x;
        if (flag)
        {
            x = null;
        }
        else
        {
            x = "hello";
        }
    }
}
