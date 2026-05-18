using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace OpenVulScan;

internal static class AdhocProjectLoader
{
    public static async Task<LoadedProject> LoadAsync(
        string projectPath,
        CompileCommands commands,
        CancellationToken ct)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var versionStamp = VersionStamp.Create();

        var preprocessorSymbols = commands.PreprocessorSymbols.ToList();
        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>(),
            allowUnsafe: true);

        if (preprocessorSymbols.Count > 0)
        {
            compilationOptions = compilationOptions.WithSpecificDiagnosticOptions(
                new Dictionary<string, ReportDiagnostic>());
        }

        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            preprocessorSymbols: preprocessorSymbols);

        var metadataReferences = new List<MetadataReference>();
        foreach (var referencePath in commands.References)
        {
            var fullPath = Path.IsPathRooted(referencePath)
                ? referencePath
                : Path.Combine(projectDirectory, referencePath);

            if (File.Exists(fullPath))
            {
                metadataReferences.Add(MetadataReference.CreateFromFile(fullPath));
            }
        }

        var projectInfo = ProjectInfo.Create(
            projectId,
            versionStamp,
            commands.AssemblyName,
            commands.AssemblyName,
            LanguageNames.CSharp,
            filePath: projectPath,
            compilationOptions: compilationOptions,
            parseOptions: parseOptions,
            metadataReferences: metadataReferences);

        var project = workspace.AddProject(projectInfo);

        foreach (var sourcePath in commands.Sources)
        {
            ct.ThrowIfCancellationRequested();

            var fullPath = Path.IsPathRooted(sourcePath)
                ? sourcePath
                : Path.Combine(projectDirectory, sourcePath);

            if (!File.Exists(fullPath))
            {
                continue;
            }

            var documentName = Path.GetFileName(fullPath);
            var documentId = DocumentId.CreateNewId(projectId);
            var sourceText = await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);

            var documentInfo = DocumentInfo.Create(
                documentId,
                documentName,
                filePath: fullPath,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(sourceText), VersionStamp.Create())));
            workspace.AddDocument(documentInfo);
        }

        project = workspace.CurrentSolution.GetProject(projectId)!;
        var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation is null)
        {
            throw new ProjectLoadException($"Failed to create compilation for project: {projectPath}");
        }

        var diagnostics = compilation.GetDiagnostics(ct).ToList();
        return new LoadedProject(commands.AssemblyName, projectPath, compilation, diagnostics);
    }
}
