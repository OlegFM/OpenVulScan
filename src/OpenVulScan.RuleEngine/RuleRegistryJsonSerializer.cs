using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenVulScan;

public static class RuleRegistryJsonSerializer
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new RuleDescriptorJsonConverter() }
    };

    public static string Serialize(IReadOnlyList<RuleDescriptor> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return JsonSerializer.Serialize(rules, s_options);
    }
}

public sealed class RuleDescriptorJsonConverter : JsonConverter<RuleDescriptor>
{
    private static readonly AnalysisCapabilityJsonConverter s_capabilityConverter = new();

    public override RuleDescriptor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object for RuleDescriptor.");
        }

        string? code = null;
        RuleSeverity? defaultLevel = null;
        string? cwe = null;
        RuleCategory? category = null;
        AnalysisCapability? capabilities = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Unexpected token {reader.TokenType} in RuleDescriptor.");
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                throw new JsonException("Unexpected end of JSON in RuleDescriptor.");
            }

            switch (propertyName)
            {
                case "code":
                    code = reader.GetString();
                    break;
                case "defaultLevel":
                    defaultLevel = Enum.Parse<RuleSeverity>(reader.GetString()!, ignoreCase: true);
                    break;
                case "cwe":
                    cwe = reader.GetString();
                    break;
                case "category":
                    category = Enum.Parse<RuleCategory>(reader.GetString()!, ignoreCase: true);
                    break;
                case "capabilities":
                    capabilities = s_capabilityConverter.Read(ref reader, typeof(AnalysisCapability), options);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (code == null || defaultLevel == null || cwe == null || category == null || capabilities == null)
        {
            throw new JsonException("Missing required properties in RuleDescriptor JSON.");
        }

        return new RuleDescriptor(code, defaultLevel.Value, cwe, category.Value, capabilities.Value, typeof(object));
    }

    public override void Write(Utf8JsonWriter writer, RuleDescriptor value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteString("code", value.Code);
        writer.WriteString("defaultLevel", value.DefaultLevel.ToString());
        writer.WriteString("cwe", value.Cwe);
        writer.WriteString("category", value.Category.ToString());

        writer.WritePropertyName("capabilities");
        s_capabilityConverter.Write(writer, value.Capabilities, options);

        writer.WriteEndObject();
    }
}

public sealed class AnalysisCapabilityJsonConverter : JsonConverter<AnalysisCapability>
{
    public override AnalysisCapability Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected start of array for AnalysisCapability.");
        }

        AnalysisCapability result = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return result;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                if (Enum.TryParse<AnalysisCapability>(value, ignoreCase: true, out var capability))
                {
                    result |= capability;
                }
                else
                {
                    throw new JsonException($"Unknown AnalysisCapability value: {value}.");
                }
            }
            else if (reader.TokenType != JsonTokenType.Null)
            {
                throw new JsonException($"Unexpected token {reader.TokenType} in AnalysisCapability array.");
            }
        }

        throw new JsonException("Expected end of array for AnalysisCapability.");
    }

    public override void Write(Utf8JsonWriter writer, AnalysisCapability value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartArray();
        foreach (AnalysisCapability capability in Enum.GetValues<AnalysisCapability>())
        {
            if (value.HasFlag(capability))
            {
                writer.WriteStringValue(capability.ToString());
            }
        }

        writer.WriteEndArray();
    }
}
