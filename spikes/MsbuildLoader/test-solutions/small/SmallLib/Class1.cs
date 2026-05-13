namespace SmallLib;

public static class MathHelper
{
    public static int Add(int a, int b) => a + b;
    public static int Subtract(int a, int b) => a - b;
    public static int Multiply(int a, int b) => a * b;
    public static int Divide(int a, int b) => a / b;
}

public class Person
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Greet() => $"Hello, my name is {Name}";
}
