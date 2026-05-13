public class TestNullables
{
    public string? ConditionalAccess(string? input)
    {
        return input?.Trim()?.ToUpper();
    }

    public string NullCoalescing(string? input)
    {
        return input ?? "default";
    }

    public string NullConditionalAssignment(string? input)
    {
        var result = input?.Trim();
        return result ?? string.Empty;
    }
}
