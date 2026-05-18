using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace OpenVulScan.Tests;

public class RulesListCommandTests
{
    [Fact]
    public async Task RulesListTextFormatReturnsTableWithHeaders()
    {
        using var stream = new MemoryStream();

        var result = await RulesListCommandHandler.ExecuteAsync("text", enabledOnly: false, stream);

        Assert.Equal(0, result);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var output = await reader.ReadToEndAsync();
        Assert.Contains("Code", output, StringComparison.Ordinal);
        Assert.Contains("Level", output, StringComparison.Ordinal);
        Assert.Contains("Category", output, StringComparison.Ordinal);
        Assert.Contains("Cwe", output, StringComparison.Ordinal);
        Assert.Contains("Capabilities", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RulesListJsonFormatReturnsParseableJson()
    {
        using var stream = new MemoryStream();

        var result = await RulesListCommandHandler.ExecuteAsync("json", enabledOnly: false, stream);

        Assert.Equal(0, result);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var output = await reader.ReadToEndAsync();
        Assert.False(string.IsNullOrWhiteSpace(output));
        var doc = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    [Fact]
    public async Task RulesListEnabledOnlyFlagReturnsSuccess()
    {
        using var stream = new MemoryStream();

        var result = await RulesListCommandHandler.ExecuteAsync("text", enabledOnly: true, stream);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task RulesListJsonContainsExpectedFields()
    {
        using var stream = new MemoryStream();

        var result = await RulesListCommandHandler.ExecuteAsync("json", enabledOnly: false, stream);

        Assert.Equal(0, result);
        stream.Position = 0;
        var doc = await JsonSerializer.DeserializeAsync<JsonElement>(stream);
        Assert.Equal(JsonValueKind.Array, doc.ValueKind);

        foreach (var element in doc.EnumerateArray())
        {
            Assert.True(element.TryGetProperty("code", out _));
            Assert.True(element.TryGetProperty("level", out _));
            Assert.True(element.TryGetProperty("category", out _));
            Assert.True(element.TryGetProperty("cwe", out _));
            Assert.True(element.TryGetProperty("capabilities", out _));
        }
    }
}
