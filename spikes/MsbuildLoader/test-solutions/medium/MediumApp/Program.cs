using System;
using System.Collections.Generic;
using MediumLib;

namespace MediumApp;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("MediumApp starting...");
        RunCalculatorDemo();
        RunStringUtilsDemo();
        RunDataProcessorDemo();
        RunInventoryDemo();
        RunConfigDemo();
        Console.WriteLine("MediumApp finished.");
    }

    static void RunCalculatorDemo()
    {
        Console.WriteLine("\n=== Calculator Demo ===");
        Console.WriteLine($"2 + 3 = {Calculator.Add(2, 3)}");
        Console.WriteLine($"10 - 4 = {Calculator.Subtract(10, 4)}");
        Console.WriteLine($"6 * 7 = {Calculator.Multiply(6, 7)}");
        Console.WriteLine($"20 / 4 = {Calculator.Divide(20, 4)}");
        Console.WriteLine($"5! = {Calculator.Factorial(5)}");
        Console.WriteLine($"Is 17 prime? {Calculator.IsPrime(17)}");
    }

    static void RunStringUtilsDemo()
    {
        Console.WriteLine("\n=== String Utils Demo ===");
        var utils = new StringUtils();
        string text = "A man a plan a canal Panama";
        Console.WriteLine($"Original: {text}");
        Console.WriteLine($"Reversed: {utils.Reverse(text)}");
        Console.WriteLine($"Is palindrome: {utils.IsPalindrome(text)}");
        Console.WriteLine($"Word count: {utils.WordCount(text)}");
        Console.WriteLine($"Slug: {utils.ToSlug(text)}");
    }

    static void RunDataProcessorDemo()
    {
        Console.WriteLine("\n=== Data Processor Demo ===");
        var processor = new DataProcessor();
        var numbers = new List<int> { 5, 2, 8, 1, 9, 3, 7, 4, 6 };
        Console.WriteLine($"Numbers: {string.Join(", ", numbers)}");
        Console.WriteLine($"Even: {string.Join(", ", processor.FilterEven(numbers))}");
        Console.WriteLine($"Odd: {string.Join(", ", processor.FilterOdd(numbers))}");
        Console.WriteLine($"Average: {processor.Average(numbers)}");
        Console.WriteLine($"Max: {processor.Max(numbers)}");
        Console.WriteLine($"Min: {processor.Min(numbers)}");
        Console.WriteLine($"Sorted: {string.Join(", ", processor.SortAscending(numbers))}");
    }

    static void RunInventoryDemo()
    {
        Console.WriteLine("\n=== Inventory Demo ===");
        var inventory = new Inventory();
        inventory.Add(new Product("Laptop", 999.99m, 5));
        inventory.Add(new Product("Mouse", 29.99m, 0));
        inventory.Add(new Product("Keyboard", 79.99m, 10));
        Console.WriteLine($"Total value: {inventory.TotalValue():C}");
        Console.WriteLine($"In stock: {inventory.InStock().Count}");
        Console.WriteLine($"Out of stock: {inventory.OutOfStock().Count}");
    }

    static void RunConfigDemo()
    {
        Console.WriteLine("\n=== Config Demo ===");
        var loader = new ConfigLoader();
        var config = new Dictionary<string, string>
        {
            ["app_name"] = "MediumApp",
            ["version"] = "1.0",
            ["max_items"] = "100",
            ["debug_mode"] = "true"
        };
        Console.WriteLine($"App name: {loader.Get(config, "app_name")}");
        Console.WriteLine($"Max items: {loader.GetInt(config, "max_items")}");
        Console.WriteLine($"Debug mode: {loader.GetBool(config, "debug_mode")}");
    }
}
