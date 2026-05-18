namespace OpenVulScan;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;

public sealed class SarifWriter
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _toolName;
    private readonly string _toolVersion;

    public SarifWriter(string toolName, string toolVersion)
    {
        _toolName = toolName;
        _toolVersion = toolVersion;
    }

    public void Write(IReadOnlyList<Diagnostic> diagnostics, IReadOnlyList<AnalysisFail> fails, IReadOnlyList<RuleDescriptor> rules, Stream output)
    {
        ArgumentNullException.ThrowIfNull(fails);

        var results = diagnostics.Select(d => new SarifResult(
            RuleId: d.Id,
            Level: MapSeverity(d.Severity),
            Message: new SarifMessage(d.GetMessage(CultureInfo.InvariantCulture)),
            Locations: d.Location.IsInSource
                ? new List<SarifLocation>
                {
                    new SarifLocation(
                        PhysicalLocation: MapLocation(d.Location))
                }
                : null))
            .ToList();

        foreach (var fail in fails)
        {
            results.Add(new SarifResult(
                RuleId: fail.Code,
                Level: "error",
                Message: new SarifMessage(fail.Message),
                Locations: fail.FilePath != null
                    ? new List<SarifLocation>
                    {
                        new SarifLocation(
                            PhysicalLocation: new SarifPhysicalLocation(
                                ArtifactLocation: new SarifArtifactLocation(
                                    Uri: fail.FilePath),
                                Region: new SarifRegion(
                                    StartLine: fail.Line ?? 1,
                                    StartColumn: 1,
                                    EndLine: fail.Line ?? 1,
                                    EndColumn: 1)))
                    }
                    : null));
        }

        var log = new SarifLog(
            Schema: "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json",
            Version: "2.1.0",
            Runs: new List<SarifRun>
            {
                new SarifRun(
                    Tool: new SarifTool(
                        Driver: new SarifToolDriver(
                            Name: _toolName,
                            Version: _toolVersion,
                            Rules: rules.DistinctBy(r => r.Code)
                                .Select(r => new SarifRule(
                                    Id: r.Code,
                                    Name: r.RuleType.Name,
                                    ShortDescription: new SarifMessage(r.RuleType.Name),
                                    Properties: new Dictionary<string, string>
                                    {
                                        { "cwe", r.Cwe },
                                        { "category", r.Category.ToString() }
                                    }))
                                .ToList())),
                    Results: results)
            });

        JsonSerializer.Serialize(output, log, s_options);
    }

    private static string MapSeverity(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        DiagnosticSeverity.Info => "note",
        DiagnosticSeverity.Hidden => "none",
        _ => "none"
    };

    private static SarifPhysicalLocation MapLocation(Location location)
    {
        var lineSpan = location.GetLineSpan();
        return new SarifPhysicalLocation(
            ArtifactLocation: new SarifArtifactLocation(
                Uri: lineSpan.Path),
            Region: new SarifRegion(
                StartLine: lineSpan.StartLinePosition.Line + 1,
                StartColumn: lineSpan.StartLinePosition.Character + 1,
                EndLine: lineSpan.EndLinePosition.Line + 1,
                EndColumn: lineSpan.EndLinePosition.Character + 1));
    }
}
