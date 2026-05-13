public class TestSwitch
{
    public string SwitchExpression(int value)
    {
        return value switch
        {
            1 => "one",
            2 => "two",
            3 => "three",
            _ => "other"
        };
    }

    public string SwitchPattern(object obj)
    {
        switch (obj)
        {
            case int i when i > 0:
                return "positive int";
            case string s when s.Length > 0:
                return "non-empty string";
            case null:
                return "null";
            default:
                return "other";
        }
    }
}
