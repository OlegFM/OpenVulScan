using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Xunit;

namespace OpenVulScan.Tests;

public class RuleRegistryTests
{
    private static readonly JsonSerializerOptions s_deserializationOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new RuleDescriptorJsonConverter() }
    };

    #region Enum Tests

    [Fact]
    public void RuleSeverityHasExpectedValues()
    {
        var values = Enum.GetValues<RuleSeverity>();
        Assert.Equal(4, values.Length);
        Assert.Contains(RuleSeverity.Level0, values);
        Assert.Contains(RuleSeverity.Level1, values);
        Assert.Contains(RuleSeverity.Level2, values);
        Assert.Contains(RuleSeverity.Level3, values);
    }

    [Fact]
    public void RuleCategoryHasExpectedValues()
    {
        var values = Enum.GetValues<RuleCategory>();
        Assert.Equal(5, values.Length);
        Assert.Contains(RuleCategory.GeneralAnalysis, values);
        Assert.Contains(RuleCategory.Owasp, values);
        Assert.Contains(RuleCategory.Unity, values);
        Assert.Contains(RuleCategory.Performance, values);
        Assert.Contains(RuleCategory.Fail, values);
    }

    [Fact]
    public void AnalysisCapabilityHasExpectedValues()
    {
        var values = Enum.GetValues<AnalysisCapability>();
        Assert.Equal(6, values.Length);
        Assert.Contains(AnalysisCapability.Ast, values);
        Assert.Contains(AnalysisCapability.Symbol, values);
        Assert.Contains(AnalysisCapability.DataFlow, values);
        Assert.Contains(AnalysisCapability.PathSensitive, values);
        Assert.Contains(AnalysisCapability.Taint, values);
        Assert.Contains(AnalysisCapability.Hierarchy, values);
    }

    [Fact]
    public void AnalysisCapabilityIsFlagsEnum()
    {
        var type = typeof(AnalysisCapability);
        var flagsAttr = type.GetCustomAttribute<FlagsAttribute>();
        Assert.NotNull(flagsAttr);
    }

    [Fact]
    public void AnalysisCapabilityCanCombineFlags()
    {
        var combined = AnalysisCapability.DataFlow | AnalysisCapability.PathSensitive;
        Assert.True(combined.HasFlag(AnalysisCapability.DataFlow));
        Assert.True(combined.HasFlag(AnalysisCapability.PathSensitive));
        Assert.False(combined.HasFlag(AnalysisCapability.Ast));
    }

    #endregion

    #region RuleAttribute Tests

    [Fact]
    public void RuleAttributeHasCorrectAttributeUsage()
    {
        var attrType = typeof(RuleAttribute);
        var usage = attrType.GetCustomAttribute<AttributeUsageAttribute>();
        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Class, usage!.ValidOn);
        Assert.False(usage.Inherited);
        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void RuleAttributeCanBeAppliedToClass()
    {
        var attr = typeof(TestRuleGeneralAnalysis).GetCustomAttribute<RuleAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("V3001", attr!.Code);
        Assert.Equal(RuleSeverity.Level1, attr.DefaultLevel);
        Assert.Equal("CWE-571", attr.Cwe);
        Assert.Equal(RuleCategory.GeneralAnalysis, attr.Category);
        Assert.Equal(AnalysisCapability.Ast, attr.Capabilities);
    }

    [Fact]
    public void RuleAttributeCannotBeAppliedToMethod()
    {
        var method = typeof(TestRuleGeneralAnalysis).GetMethods().First(m => m.Name == "ToString");
        var attr = method.GetCustomAttribute<RuleAttribute>();
        Assert.Null(attr);
    }

    [Fact]
    public void RuleAttributeStoresMultipleCapabilities()
    {
        var attr = typeof(TestRuleOwasp).GetCustomAttribute<RuleAttribute>();
        Assert.NotNull(attr);
        Assert.True(attr!.Capabilities.HasFlag(AnalysisCapability.DataFlow));
        Assert.True(attr.Capabilities.HasFlag(AnalysisCapability.PathSensitive));
    }

    #endregion

    #region RuleDescriptor Tests

    [Fact]
    public void RuleDescriptorContainsAllMetadata()
    {
        var attr = typeof(TestRuleGeneralAnalysis).GetCustomAttribute<RuleAttribute>()!;
        var descriptor = new RuleDescriptor(
            attr.Code,
            attr.DefaultLevel,
            attr.Cwe,
            attr.Category,
            attr.Capabilities,
            typeof(TestRuleGeneralAnalysis));

        Assert.Equal("V3001", descriptor.Code);
        Assert.Equal(RuleSeverity.Level1, descriptor.DefaultLevel);
        Assert.Equal("CWE-571", descriptor.Cwe);
        Assert.Equal(RuleCategory.GeneralAnalysis, descriptor.Category);
        Assert.Equal(AnalysisCapability.Ast, descriptor.Capabilities);
        Assert.Equal(typeof(TestRuleGeneralAnalysis), descriptor.RuleType);
    }

    [Fact]
    public void RuleDescriptorImplementsEquality()
    {
        var d1 = new RuleDescriptor("V3001", RuleSeverity.Level1, "CWE-571", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast, typeof(TestRuleGeneralAnalysis));
        var d2 = new RuleDescriptor("V3001", RuleSeverity.Level1, "CWE-571", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast, typeof(TestRuleGeneralAnalysis));
        var d3 = new RuleDescriptor("V3002", RuleSeverity.Level1, "CWE-571", RuleCategory.GeneralAnalysis, AnalysisCapability.Ast, typeof(TestRuleGeneralAnalysis));

        Assert.Equal(d1, d2);
        Assert.NotEqual(d1, d3);
    }

    #endregion

    #region RuleRegistry Scan Tests

    [Fact]
    public void RuleRegistryScanAssemblyFindsRules()
    {
        var registry = new RuleRegistry();
        registry.Scan(typeof(TestRuleGeneralAnalysis).Assembly);

        var all = registry.GetAll();
        Assert.Equal(5, all.Count);
    }

    [Fact]
    public void RuleRegistryScanMultipleAssembliesFindsRules()
    {
        var registry = new RuleRegistry();
        var assemblies = new[] { typeof(TestRuleGeneralAnalysis).Assembly };
        registry.Scan(assemblies);

        var all = registry.GetAll();
        Assert.Equal(5, all.Count);
    }

    [Fact]
    public void RuleRegistryScanNullAssemblyThrowsArgumentNullException()
    {
        var registry = new RuleRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Scan((Assembly)null!));
    }

    [Fact]
    public void RuleRegistryScanNullAssembliesThrowsArgumentNullException()
    {
        var registry = new RuleRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Scan((IEnumerable<Assembly>)null!));
    }

    [Fact]
    public void RuleRegistryScanAssemblyWithNoRulesDoesNotThrow()
    {
        var registry = new RuleRegistry();
        registry.Scan(typeof(RuleRegistry).Assembly);

        var all = registry.GetAll();
        Assert.Empty(all);
    }

    #endregion

    #region RuleRegistry Query Tests

    [Fact]
    public void RuleRegistryGetByCodeReturnsCorrectRule()
    {
        var registry = new RuleRegistry();
        registry.Scan(typeof(TestRuleGeneralAnalysis).Assembly);

        var rule = registry.GetByCode("V3001");
        Assert.NotNull(rule);
        Assert.Equal("V3001", rule!.Code);
        Assert.Equal(typeof(TestRuleGeneralAnalysis), rule.RuleType);
    }

    [Fact]
    public void RuleRegistryGetByCodeReturnsNullForMissingCode()
    {
        var registry = new RuleRegistry();
        registry.Scan(typeof(TestRuleGeneralAnalysis).Assembly);

        var rule = registry.GetByCode("V9999");
        Assert.Null(rule);
    }

    [Fact]
    public void RuleRegistryGetByCodeNullCodeThrowsArgumentNullException()
    {
        var registry = new RuleRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.GetByCode(null!));
    }

    [Fact]
    public void RuleRegistryGetByCategoryReturnsRulesInCategory()
    {
        var registry = new RuleRegistry();
        registry.Scan(typeof(TestRuleGeneralAnalysis).Assembly);

        var owaspRules = registry.GetByCategory(RuleCategory.Owasp);
        Assert.Single(owaspRules);
        Assert.Equal("V3002", owaspRules[0].Code);
    }

    [Fact]
    public void RuleRegistryGetByCategoryReturnsEmptyForNoMatches()
    {
        var registry = new RuleRegistry();

        var rules = registry.GetByCategory(RuleCategory.GeneralAnalysis);
        Assert.Empty(rules);
    }

    [Fact]
    public void RuleRegistryGetAllReturnsAllRegisteredRules()
    {
        var registry = new RuleRegistry();
        registry.Scan(typeof(TestRuleGeneralAnalysis).Assembly);

        var all = registry.GetAll();
        Assert.Equal(5, all.Count);
        Assert.Contains(all, r => r.Code == "V3001");
        Assert.Contains(all, r => r.Code == "V3002");
        Assert.Contains(all, r => r.Code == "V3003");
        Assert.Contains(all, r => r.Code == "V3004");
        Assert.Contains(all, r => r.Code == "V3005");
    }

    [Fact]
    public void RuleRegistryGetAllReturnsEmptyWhenNoRules()
    {
        var registry = new RuleRegistry();
        Assert.Empty(registry.GetAll());
    }

    #endregion

    #region RuleRegistry Deduplication Tests

    [Fact]
    public void RuleRegistryDuplicateCodeThrowsInvalidOperationException()
    {
        var registry = new RuleRegistry();
        registry.Scan(typeof(TestRuleGeneralAnalysis).Assembly);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.Scan(typeof(TestRuleGeneralAnalysis).Assembly));

        Assert.Contains("V3001", ex.Message, StringComparison.Ordinal);
        Assert.Contains("already registered", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleRegistryDuplicateCodeKeepsOriginalRules()
    {
        var registry = new RuleRegistry();
        registry.Scan(typeof(TestRuleGeneralAnalysis).Assembly);

        try
        {
            registry.Scan(typeof(TestRuleGeneralAnalysis).Assembly);
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        var all = registry.GetAll();
        Assert.Equal(5, all.Count);
    }

    #endregion

    #region RuleRegistry Thread Safety Tests

    [Fact]
    public void RuleRegistryConcurrentScanDoesNotCorruptRegistry()
    {
        var registry = new RuleRegistry();
        var assembly = typeof(TestRuleGeneralAnalysis).Assembly;

        Parallel.For(0, 10, _ =>
        {
            try
            {
                registry.Scan(assembly);
            }
            catch (InvalidOperationException)
            {
                // Expected due to deduplication
            }
        });

        var all = registry.GetAll();
        Assert.Equal(5, all.Count);
    }

    [Fact]
    public void RuleRegistryConcurrentScanMultipleInstancesIsolated()
    {
        var registries = new RuleRegistry[10];
        for (int i = 0; i < registries.Length; i++)
        {
            registries[i] = new RuleRegistry();
        }

        var assembly = typeof(TestRuleGeneralAnalysis).Assembly;

        Parallel.For(0, 10, i =>
        {
            registries[i].Scan(assembly);
        });

        foreach (var registry in registries)
        {
            Assert.Equal(5, registry.GetAll().Count);
        }
    }

    #endregion

    #region JSON Serialization Tests

    [Fact]
    public void RuleRegistryJsonSerializerSerializesToCorrectShape()
    {
        var registry = new RuleRegistry();
        registry.Scan(typeof(TestRuleGeneralAnalysis).Assembly);

        var json = RuleRegistryJsonSerializer.Serialize(registry.GetAll());
        using var doc = JsonDocument.Parse(json);

        var rules = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(5, rules.Count);

        var firstRule = rules.First(r => r.GetProperty("code").GetString() == "V3001");
        Assert.Equal("Level1", firstRule.GetProperty("defaultLevel").GetString());
        Assert.Equal("CWE-571", firstRule.GetProperty("cwe").GetString());
        Assert.Equal("GeneralAnalysis", firstRule.GetProperty("category").GetString());

        var capabilities = firstRule.GetProperty("capabilities").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Single(capabilities);
        Assert.Equal("Ast", capabilities[0]);
    }

    [Fact]
    public void RuleRegistryJsonSerializerSerializesMultipleCapabilitiesAsArray()
    {
        var registry = new RuleRegistry();
        registry.Scan(typeof(TestRuleOwasp).Assembly);

        var json = RuleRegistryJsonSerializer.Serialize(registry.GetAll());
        using var doc = JsonDocument.Parse(json);

        var rule = doc.RootElement.EnumerateArray().First(r => r.GetProperty("code").GetString() == "V3002");
        var capabilities = rule.GetProperty("capabilities").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(2, capabilities.Count);
        Assert.Contains("DataFlow", capabilities);
        Assert.Contains("PathSensitive", capabilities);
    }

    [Fact]
    public void RuleRegistryJsonSerializerRoundtripMaintainsData()
    {
        var registry = new RuleRegistry();
        registry.Scan(typeof(TestRuleGeneralAnalysis).Assembly);
        var originalRules = registry.GetAll();

        var json = RuleRegistryJsonSerializer.Serialize(originalRules);
        var deserializedRules = JsonSerializer.Deserialize<List<RuleDescriptor>>(json, s_deserializationOptions);

        Assert.NotNull(deserializedRules);
        Assert.Equal(originalRules.Count, deserializedRules!.Count);

        foreach (var original in originalRules)
        {
            var deserialized = deserializedRules.FirstOrDefault(r => r.Code == original.Code);
            Assert.NotNull(deserialized);
            Assert.Equal(original.DefaultLevel, deserialized.DefaultLevel);
            Assert.Equal(original.Cwe, deserialized.Cwe);
            Assert.Equal(original.Category, deserialized.Category);
            Assert.Equal(original.Capabilities, deserialized.Capabilities);
        }
    }

    [Fact]
    public void RuleRegistryJsonSerializerSerializesEmptyList()
    {
        var json = RuleRegistryJsonSerializer.Serialize(new List<RuleDescriptor>());
        Assert.Equal("[]", json);
    }

    [Fact]
    public void RuleRegistryJsonSerializerNullRulesThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RuleRegistryJsonSerializer.Serialize(null!));
    }

    #endregion

    #region AnalysisCapabilityJsonConverter Tests

    public static TheoryData<string, AnalysisCapability> AnalysisCapabilityRoundtripData => new()
    {
        { "[\"Ast\"]", AnalysisCapability.Ast },
        { "[\"DataFlow\", \"PathSensitive\"]", AnalysisCapability.DataFlow | AnalysisCapability.PathSensitive },
        { "[]", 0 }
    };

    [Theory]
    [MemberData(nameof(AnalysisCapabilityRoundtripData))]
    public void AnalysisCapabilityJsonConverterRoundtrip(string json, AnalysisCapability expected)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new AnalysisCapabilityJsonConverter());

        var result = JsonSerializer.Deserialize<AnalysisCapability>(json, options);
        Assert.Equal(expected, result);

        var serialized = JsonSerializer.Serialize(result, options);
        var reparsed = JsonSerializer.Deserialize<AnalysisCapability>(serialized, options);
        Assert.Equal(expected, reparsed);
    }

    [Fact]
    public void AnalysisCapabilityJsonConverterInvalidValueThrowsJsonException()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new AnalysisCapabilityJsonConverter());

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AnalysisCapability>("[\"InvalidCapability\"]", options));
    }

    [Fact]
    public void AnalysisCapabilityJsonConverterNotArrayThrowsJsonException()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new AnalysisCapabilityJsonConverter());

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AnalysisCapability>("\"Ast\"", options));
    }

    #endregion
}
