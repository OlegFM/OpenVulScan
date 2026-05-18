using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OpenVulScan.Tests;

public class BaselineSuppressionTests
{
    private static Diagnostic CreateDiagnostic(string id, string filePath, int line, string lineContent)
    {
        var lines = new List<string>();
        for (var i = 0; i < line - 1; i++)
        {
            lines.Add("");
        }

        lines.Add(lineContent);

        var source = string.Join("\n", lines);
        var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var root = tree.GetRoot();
        var node = root.DescendantNodes().OfType<VariableDeclarationSyntax>().FirstOrDefault()
            ?? root.DescendantNodes().OfType<ExpressionStatementSyntax>().FirstOrDefault()
            ?? root.DescendantNodes().First();

        return Diagnostic.Create(
            new DiagnosticDescriptor(id, "Test", "Test message", "Test", DiagnosticSeverity.Warning, true),
            node.GetLocation());
    }

    private static string ComputeFingerprintForLine(string lineContent)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(lineContent));
        return Convert.ToHexString(hash)[..8];
    }

    [Fact]
    public void ExactMatchSuppressesDiagnostic()
    {
        var diagnostics = new List<Diagnostic>
        {
            CreateDiagnostic("V3001", "test.cs", 5, "int x = 1;")
        };
        var fingerprint = BaselineFile.ComputeFingerprint(diagnostics[0]);
        var baseline = new List<BaselineEntry>
        {
            new("V3001", "test.cs", 5, 1, fingerprint)
        };

        var result = BaselineFilter.Apply(diagnostics, baseline);

        Assert.Empty(result);
    }

    [Fact]
    public void MovedBy3LinesStillSuppresses()
    {
        var diagnostics = new List<Diagnostic>
        {
            CreateDiagnostic("V3001", "test.cs", 8, "int x = 1;")
        };
        var fingerprint = BaselineFile.ComputeFingerprint(diagnostics[0]);
        var baseline = new List<BaselineEntry>
        {
            new("V3001", "test.cs", 5, 1, fingerprint)
        };

        var result = BaselineFilter.Apply(diagnostics, baseline);

        Assert.Empty(result);
    }

    [Fact]
    public void MovedBy10LinesDoesNotSuppress()
    {
        var diagnostics = new List<Diagnostic>
        {
            CreateDiagnostic("V3001", "test.cs", 15, "int x = 1;")
        };
        var fingerprint = BaselineFile.ComputeFingerprint(diagnostics[0]);
        var baseline = new List<BaselineEntry>
        {
            new("V3001", "test.cs", 5, 1, fingerprint)
        };

        var result = BaselineFilter.Apply(diagnostics, baseline);

        Assert.Single(result);
    }

    [Fact]
    public void ChangedContentDoesNotSuppress()
    {
        var diagnostics = new List<Diagnostic>
        {
            CreateDiagnostic("V3001", "test.cs", 5, "int y = 2;")
        };
        var baseline = new List<BaselineEntry>
        {
            new("V3001", "test.cs", 5, 1, "OLDHASH1")
        };

        var result = BaselineFilter.Apply(diagnostics, baseline);

        Assert.Single(result);
    }

    [Fact]
    public void DifferentRuleCodeDoesNotSuppress()
    {
        var diagnostics = new List<Diagnostic>
        {
            CreateDiagnostic("V3002", "test.cs", 5, "int x = 1;")
        };
        var fingerprint = BaselineFile.ComputeFingerprint(diagnostics[0]);
        var baseline = new List<BaselineEntry>
        {
            new("V3001", "test.cs", 5, 1, fingerprint)
        };

        var result = BaselineFilter.Apply(diagnostics, baseline);

        Assert.Single(result);
    }

    [Fact]
    public void DifferentFilePathDoesNotSuppress()
    {
        var diagnostics = new List<Diagnostic>
        {
            CreateDiagnostic("V3001", "other.cs", 5, "int x = 1;")
        };
        var fingerprint = BaselineFile.ComputeFingerprint(diagnostics[0]);
        var baseline = new List<BaselineEntry>
        {
            new("V3001", "test.cs", 5, 1, fingerprint)
        };

        var result = BaselineFilter.Apply(diagnostics, baseline);

        Assert.Single(result);
    }

    [Fact]
    public void WhitespaceNormalizationKeepsFingerprintStable()
    {
        var diagnostic1 = CreateDiagnostic("V3001", "test.cs", 5, "int    x = 1;");
        var diagnostic2 = CreateDiagnostic("V3001", "test.cs", 5, "int x = 1;");

        var fp1 = BaselineFile.ComputeFingerprint(diagnostic1);
        var fp2 = BaselineFile.ComputeFingerprint(diagnostic2);

        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void BackslashPathMatchesForwardSlashPath()
    {
        var diagnostics = new List<Diagnostic>
        {
            CreateDiagnostic("V3001", "src/test.cs", 5, "int x = 1;")
        };
        var fingerprint = BaselineFile.ComputeFingerprint(diagnostics[0]);
        var baseline = new List<BaselineEntry>
        {
            new("V3001", "src\\test.cs", 5, 1, fingerprint)
        };

        var result = BaselineFilter.Apply(diagnostics, baseline);

        Assert.Empty(result);
    }

    [Fact]
    public async Task EndToEndBaselineSuppressesMovedDiagnostic()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var projectPath = Path.Combine(tempDir, "Synthetic.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
                "  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>\n" +
                "</Project>");

            var sourceDir = Path.Combine(tempDir, "src");
            Directory.CreateDirectory(sourceDir);
            var sourcePath = Path.Combine(sourceDir, "Test.cs");
            await File.WriteAllTextAsync(
                sourcePath,
                "public class Test\n" +
                "{\n" +
                "    public void M()\n" +
                "    {\n" +
                "        int a = 1;\n" +
                "        if (a == a) { }\n" +
                "    }\n" +
                "}\n");

        var baselinePath = Path.Combine(tempDir, "baseline.suppress");

        try
        {
            // Create baseline
            var createResult = await BaselineCommandHandler.CreateAsync(projectPath, baselinePath, CancellationToken.None);
            Assert.Equal(0, createResult);

            // Verify baseline has the diagnostic
            var baselineEntries = BaselineFile.Read(baselinePath);
            Assert.NotEmpty(baselineEntries);

            // Move the line by 3 lines (insert empty lines before it)
            await File.WriteAllTextAsync(
                sourcePath,
                "public class Test\n" +
                "{\n" +
                "    public void M()\n" +
                "    {\n" +
                "\n" +
                "\n" +
                "\n" +
                "        int a = 1;\n" +
                "        if (a == a) { }\n" +
                "    }\n" +
                "}\n");

            // Run analysis with baseline
            var (diagnostics, _) = await AnalysisRunner.RunAnalysisAsync(
                projectPath, null, null, baselinePath, CancellationToken.None);

            // The V3001 diagnostic should be suppressed by baseline even after moving 3 lines
            Assert.DoesNotContain(diagnostics, d => d.Id == "V3001");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DiagnosticNotInSourceIsNotSuppressed()
    {
        var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor("V3001", "Test", "Test", "Test", DiagnosticSeverity.Warning, true),
            Location.None);
        var diagnostics = new List<Diagnostic> { diagnostic };
        var baseline = new List<BaselineEntry>
        {
            new("V3001", "test.cs", 5, 1, "ABCD1234")
        };

        var result = BaselineFilter.Apply(diagnostics, baseline);

        Assert.Single(result);
    }
}
