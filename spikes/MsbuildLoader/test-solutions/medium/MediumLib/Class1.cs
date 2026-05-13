using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MediumLib;

public static class Calculator
{
    public static int Add(int a, int b) => a + b;
    public static int Subtract(int a, int b) => a - b;
    public static int Multiply(int a, int b) => a * b;
    public static double Divide(int a, int b) => b == 0 ? throw new DivideByZeroException() : (double)a / b;
    public static int Modulo(int a, int b) => a % b;
    public static int Power(int baseValue, int exponent)
    {
        int result = 1;
        for (int i = 0; i < exponent; i++)
        {
            result *= baseValue;
        }
        return result;
    }
    public static int Factorial(int n)
    {
        if (n < 0) throw new ArgumentException("n must be non-negative");
        return n <= 1 ? 1 : n * Factorial(n - 1);
    }
    public static bool IsPrime(int n)
    {
        if (n < 2) return false;
        for (int i = 2; i * i <= n; i++)
        {
            if (n % i == 0) return false;
        }
        return true;
    }
    public static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            int temp = b;
            b = a % b;
            a = temp;
        }
        return a;
    }
    public static int Lcm(int a, int b) => Math.Abs(a * b) / Gcd(a, b);
}

public class StringUtils
{
    public string Reverse(string input)
    {
        char[] chars = input.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }
    public bool IsPalindrome(string input)
    {
        string cleaned = new string(input.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return cleaned == this.Reverse(cleaned);
    }
    public int WordCount(string input) => input.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
    public string Truncate(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input) || input.Length <= maxLength) return input;
        return input[..maxLength] + "...";
    }
    public string ToSlug(string input)
    {
        return new string(input.ToLower().Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Replace(' ', '-');
    }
}

public class DataProcessor
{
    public List<int> FilterEven(List<int> numbers) => numbers.Where(n => n % 2 == 0).ToList();
    public List<int> FilterOdd(List<int> numbers) => numbers.Where(n => n % 2 != 0).ToList();
    public double Average(List<int> numbers) => numbers.Count == 0 ? 0 : numbers.Average();
    public int Max(List<int> numbers) => numbers.Count == 0 ? 0 : numbers.Max();
    public int Min(List<int> numbers) => numbers.Count == 0 ? 0 : numbers.Min();
    public List<int> SortAscending(List<int> numbers) => numbers.OrderBy(n => n).ToList();
    public List<int> SortDescending(List<int> numbers) => numbers.OrderByDescending(n => n).ToList();
    public Dictionary<int, int> Frequency(List<int> numbers)
    {
        var dict = new Dictionary<int, int>();
        foreach (var n in numbers)
        {
            if (!dict.ContainsKey(n)) dict[n] = 0;
            dict[n]++;
        }
        return dict;
    }
}

public record Product(string Name, decimal Price, int Quantity)
{
    public decimal TotalValue => Price * Quantity;
    public bool IsInStock => Quantity > 0;
    public string FormattedPrice => $"${Price:F2}";
}

public class Inventory
{
    private readonly List<Product> _products = [];
    public void Add(Product product) => _products.Add(product);
    public void Remove(string name) => _products.RemoveAll(p => p.Name == name);
    public Product? Find(string name) => _products.FirstOrDefault(p => p.Name == name);
    public decimal TotalValue() => _products.Sum(p => p.TotalValue);
    public List<Product> InStock() => _products.Where(p => p.IsInStock).ToList();
    public List<Product> OutOfStock() => _products.Where(p => !p.IsInStock).ToList();
}

public interface ILogger
{
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message);
}

public class ConsoleLogger : ILogger
{
    public void LogInfo(string message) => Console.WriteLine($"[INFO] {message}");
    public void LogWarning(string message) => Console.WriteLine($"[WARN] {message}");
    public void LogError(string message) => Console.WriteLine($"[ERROR] {message}");
}

public class ConfigLoader
{
    public Dictionary<string, string> Load(string path)
    {
        var config = new Dictionary<string, string>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            var parts = line.Split('=', 2);
            if (parts.Length == 2)
            {
                config[parts[0].Trim()] = parts[1].Trim();
            }
        }
        return config;
    }
    public string? Get(Dictionary<string, string> config, string key) => config.TryGetValue(key, out var value) ? value : null;
    public int GetInt(Dictionary<string, string> config, string key, int defaultValue = 0)
    {
        var val = Get(config, key);
        return int.TryParse(val, out var result) ? result : defaultValue;
    }
    public bool GetBool(Dictionary<string, string> config, string key, bool defaultValue = false)
    {
        var val = Get(config, key);
        return bool.TryParse(val, out var result) ? result : defaultValue;
    }
}
