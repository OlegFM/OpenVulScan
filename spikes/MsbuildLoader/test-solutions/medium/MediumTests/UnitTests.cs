using System;
using System.Collections.Generic;
using Xunit;
using MediumLib;

namespace MediumTests;

public class CalculatorTests
{
    [Fact]
    public void Add_ReturnsSum()
    {
        Assert.Equal(5, Calculator.Add(2, 3));
    }

    [Fact]
    public void Subtract_ReturnsDifference()
    {
        Assert.Equal(1, Calculator.Subtract(3, 2));
    }

    [Fact]
    public void Multiply_ReturnsProduct()
    {
        Assert.Equal(6, Calculator.Multiply(2, 3));
    }

    [Fact]
    public void Divide_ReturnsQuotient()
    {
        Assert.Equal(2.5, Calculator.Divide(5, 2));
    }

    [Fact]
    public void Modulo_ReturnsRemainder()
    {
        Assert.Equal(1, Calculator.Modulo(5, 2));
    }

    [Fact]
    public void Power_ReturnsExponentiation()
    {
        Assert.Equal(8, Calculator.Power(2, 3));
    }

    [Fact]
    public void Factorial_ReturnsCorrectValue()
    {
        Assert.Equal(120, Calculator.Factorial(5));
    }

    [Fact]
    public void IsPrime_DetectsPrimes()
    {
        Assert.True(Calculator.IsPrime(7));
        Assert.False(Calculator.IsPrime(4));
    }

    [Fact]
    public void Gcd_ReturnsGreatestCommonDivisor()
    {
        Assert.Equal(6, Calculator.Gcd(54, 24));
    }

    [Fact]
    public void Lcm_ReturnsLeastCommonMultiple()
    {
        Assert.Equal(12, Calculator.Lcm(4, 6));
    }
}

public class StringUtilsTests
{
    private readonly StringUtils _utils = new();

    [Fact]
    public void Reverse_ReturnsReversedString()
    {
        Assert.Equal("cba", _utils.Reverse("abc"));
    }

    [Fact]
    public void IsPalindrome_DetectsPalindromes()
    {
        Assert.True(_utils.IsPalindrome("A man a plan a canal Panama"));
        Assert.False(_utils.IsPalindrome("hello"));
    }

    [Fact]
    public void WordCount_CountsWords()
    {
        Assert.Equal(3, _utils.WordCount("one two three"));
    }

    [Fact]
    public void Truncate_LimitsLength()
    {
        Assert.Equal("hel...", _utils.Truncate("hello world", 3));
    }

    [Fact]
    public void ToSlug_FormatsSlug()
    {
        Assert.Equal("hello-world", _utils.ToSlug("Hello World!"));
    }
}

public class DataProcessorTests
{
    private readonly DataProcessor _processor = new();
    private readonly List<int> _data = [1, 2, 3, 4, 5, 6];

    [Fact]
    public void FilterEven_ReturnsEvenNumbers()
    {
        Assert.Equal([2, 4, 6], _processor.FilterEven(_data));
    }

    [Fact]
    public void FilterOdd_ReturnsOddNumbers()
    {
        Assert.Equal([1, 3, 5], _processor.FilterOdd(_data));
    }

    [Fact]
    public void Average_ReturnsMean()
    {
        Assert.Equal(3.5, _processor.Average(_data));
    }

    [Fact]
    public void Max_ReturnsMaximum()
    {
        Assert.Equal(6, _processor.Max(_data));
    }

    [Fact]
    public void Min_ReturnsMinimum()
    {
        Assert.Equal(1, _processor.Min(_data));
    }

    [Fact]
    public void SortAscending_OrdersCorrectly()
    {
        Assert.Equal([1, 2, 3, 4, 5, 6], _processor.SortAscending([6, 5, 4, 3, 2, 1]));
    }

    [Fact]
    public void SortDescending_OrdersCorrectly()
    {
        Assert.Equal([6, 5, 4, 3, 2, 1], _processor.SortDescending([1, 2, 3, 4, 5, 6]));
    }

    [Fact]
    public void Frequency_CountsOccurrences()
    {
        var freq = _processor.Frequency([1, 1, 2, 2, 2]);
        Assert.Equal(2, freq[1]);
        Assert.Equal(3, freq[2]);
    }
}

public class InventoryTests
{
    private readonly Inventory _inventory = new();

    [Fact]
    public void Add_IncreasesCount()
    {
        _inventory.Add(new Product("Widget", 9.99m, 10));
        Assert.NotNull(_inventory.Find("Widget"));
    }

    [Fact]
    public void TotalValue_SumsCorrectly()
    {
        _inventory.Add(new Product("A", 10m, 2));
        _inventory.Add(new Product("B", 5m, 4));
        Assert.Equal(40m, _inventory.TotalValue());
    }

    [Fact]
    public void InStock_FiltersCorrectly()
    {
        _inventory.Add(new Product("A", 1m, 1));
        _inventory.Add(new Product("B", 1m, 0));
        Assert.Single(_inventory.InStock());
    }
}

public class ConfigLoaderTests
{
    private readonly ConfigLoader _loader = new();

    [Fact]
    public void GetInt_ParsesInteger()
    {
        var config = new Dictionary<string, string> { ["timeout"] = "30" };
        Assert.Equal(30, _loader.GetInt(config, "timeout"));
    }

    [Fact]
    public void GetBool_ParsesBoolean()
    {
        var config = new Dictionary<string, string> { ["enabled"] = "true" };
        Assert.True(_loader.GetBool(config, "enabled"));
    }
}
