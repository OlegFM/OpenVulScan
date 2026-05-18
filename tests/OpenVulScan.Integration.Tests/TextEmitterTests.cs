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

public class TextEmitterTests
{
    [Fact]
    public async Task WriteAsyncWithNoDiagnosticsAndNoFailsWritesNothing()
    {
        using var stream = new MemoryStream();
        var diagnostics = new List<Diagnostic>();
        var fails = new List<AnalysisFail>();

        await TextEmitter.WriteAsync(diagnostics, fails, stream, CancellationToken.None);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var output = await reader.ReadToEndAsync();
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public async Task WriteAsyncWithOneDiagnosticWithLocation()
    {
        using var stream = new MemoryStream();

        var source = "class C { void M() {} }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "src/Foo.cs");
        var root = await tree.GetRootAsync();
        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
        var location = classDecl.Identifier.GetLocation();

        var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor("V3001", "Title", "Message", "Category", DiagnosticSeverity.Warning, true),
            location);

        var diagnostics = new List<Diagnostic> { diagnostic };
        var fails = new List<AnalysisFail>();

        await TextEmitter.WriteAsync(diagnostics, fails, stream, CancellationToken.None);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var output = await reader.ReadToEndAsync();
        Assert.Equal("src/Foo.cs(1,7): [WARNING] V3001: Message" + Environment.NewLine, output);
    }

    [Fact]
    public async Task WriteAsyncWithOneDiagnosticWithoutLocation()
    {
        using var stream = new MemoryStream();

        var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor("V3002", "Title", "No location message", "Category", DiagnosticSeverity.Error, true),
            Location.None);

        var diagnostics = new List<Diagnostic> { diagnostic };
        var fails = new List<AnalysisFail>();

        await TextEmitter.WriteAsync(diagnostics, fails, stream, CancellationToken.None);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var output = await reader.ReadToEndAsync();
        Assert.Equal("[ERROR] V3002: No location message" + Environment.NewLine, output);
    }

    [Fact]
    public async Task WriteAsyncWithMultipleDiagnosticsAndFails()
    {
        using var stream = new MemoryStream();

        var source = "class C { void M() {} }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "src/Foo.cs");
        var root = await tree.GetRootAsync();
        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
        var location = classDecl.Identifier.GetLocation();

        var diagnostic1 = Diagnostic.Create(
            new DiagnosticDescriptor("V3001", "Title", "Message1", "Category", DiagnosticSeverity.Warning, true),
            location);

        var diagnostic2 = Diagnostic.Create(
            new DiagnosticDescriptor("V3002", "Title", "Message2", "Category", DiagnosticSeverity.Error, true),
            Location.None);

        var diagnostics = new List<Diagnostic> { diagnostic1, diagnostic2 };
        var fails = new List<AnalysisFail>
        {
            new("F001", "Fail message", "src/Bar.cs", 5),
            new("F002", "Fail no path", null, null),
            new("F003", "Fail no line", "src/Baz.cs", null)
        };

        await TextEmitter.WriteAsync(diagnostics, fails, stream, CancellationToken.None);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var output = await reader.ReadToEndAsync();
        var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(5, lines.Length);
        Assert.Equal("src/Foo.cs(1,7): [WARNING] V3001: Message1", lines[0]);
        Assert.Equal("[ERROR] V3002: Message2", lines[1]);
        Assert.Equal("src/Bar.cs(5,1): [FAIL] F001: Fail message", lines[2]);
        Assert.Equal("[FAIL] F002: Fail no path", lines[3]);
        Assert.Equal("src/Baz.cs: [FAIL] F003: Fail no line", lines[4]);
    }
}
