using Microsoft.CodeAnalysis;

namespace OpenVulScan;

internal sealed class BaselineCommandHandler
{
    public static async Task<int> CreateAsync(
        string path,
        string? outputPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var (diagnostics, _) = await AnalysisRunner.RunAnalysisAsync(path, null, null, cancellationToken).ConfigureAwait(false);
            var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
            var entries = diagnostics.Select(d => ToBaselineEntry(d, baseDirectory)).ToList();
            var targetPath = string.IsNullOrEmpty(outputPath) ? "openvulscan.suppress" : outputPath;
            BaselineFile.Write(targetPath, entries);
            await Console.Out.WriteLineAsync($"Baseline created with {entries.Count} entries: {Path.GetFullPath(targetPath)}").ConfigureAwait(false);
            return 0;
        }
        catch (ProjectLoadException ex)
        {
            await Console.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
            return 2;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
            return 2;
        }
#pragma warning restore CA1031
    }

    public static async Task<int> UpdateAsync(
        string path,
        string? baselinePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var targetPath = string.IsNullOrEmpty(baselinePath) ? "openvulscan.suppress" : baselinePath;
            var existing = BaselineFile.Read(targetPath);
            var (diagnostics, _) = await AnalysisRunner.RunAnalysisAsync(path, null, null, cancellationToken).ConfigureAwait(false);
            var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
            var current = diagnostics.Select(d => ToBaselineEntry(d, baseDirectory)).ToList();

            var merged = MergeEntries(existing, current);
            BaselineFile.Write(targetPath, merged);

            var added = merged.Count - existing.Count;
            await Console.Out.WriteLineAsync($"Baseline updated: {added} new entries, {existing.Count} preserved, {merged.Count} total.").ConfigureAwait(false);
            return 0;
        }
        catch (ProjectLoadException ex)
        {
            await Console.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
            return 2;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
            return 2;
        }
#pragma warning restore CA1031
    }

    public static async Task<int> DiffAsync(
        string path,
        string? baselinePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var targetPath = string.IsNullOrEmpty(baselinePath) ? "openvulscan.suppress" : baselinePath;
            var existing = BaselineFile.Read(targetPath);
            var (diagnostics, _) = await AnalysisRunner.RunAnalysisAsync(path, null, null, cancellationToken).ConfigureAwait(false);
            var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
            var current = diagnostics.Select(d => ToBaselineEntry(d, baseDirectory)).ToList();

            var existingSet = new HashSet<string>(existing.Select(ToKey));
            var currentSet = new HashSet<string>(current.Select(ToKey));

            var added = current.Where(c => !existingSet.Contains(ToKey(c))).ToList();
            var removed = existing.Where(e => !currentSet.Contains(ToKey(e))).ToList();

            foreach (var entry in added)
            {
                await Console.Out.WriteLineAsync($"+ {entry.RuleCode} {entry.FilePath}:{entry.Line}:{entry.Column}").ConfigureAwait(false);
            }

            foreach (var entry in removed)
            {
                await Console.Out.WriteLineAsync($"- {entry.RuleCode} {entry.FilePath}:{entry.Line}:{entry.Column}").ConfigureAwait(false);
            }

            if (added.Count == 0 && removed.Count == 0)
            {
                await Console.Out.WriteLineAsync("No differences found.").ConfigureAwait(false);
            }
            else
            {
                await Console.Out.WriteLineAsync($"{added.Count} added, {removed.Count} removed.").ConfigureAwait(false);
            }

            return added.Count > 0 ? 1 : 0;
        }
        catch (ProjectLoadException ex)
        {
            await Console.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
            return 2;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
            return 2;
        }
#pragma warning restore CA1031
    }

    private static BaselineEntry ToBaselineEntry(Diagnostic diagnostic, string baseDirectory)
    {
        var lineSpan = diagnostic.Location.GetLineSpan();
        var path = lineSpan.Path ?? string.Empty;
        if (!string.IsNullOrEmpty(path) && Path.IsPathRooted(path))
        {
            path = Path.GetRelativePath(baseDirectory, path);
            path = path.Replace("\\", "/", StringComparison.Ordinal);
        }

        var line = lineSpan.StartLinePosition.Line + 1;
        var column = lineSpan.StartLinePosition.Character + 1;
        var fingerprint = BaselineFile.ComputeFingerprint(diagnostic);

        return new BaselineEntry(diagnostic.Id, path, line, column, fingerprint);
    }

    private static string ToKey(BaselineEntry entry)
    {
        return $"{entry.RuleCode}|{entry.FilePath}|{entry.Line}|{entry.Column}|{entry.Fingerprint}";
    }

    private static List<BaselineEntry> MergeEntries(
        IReadOnlyList<BaselineEntry> existing,
        IReadOnlyList<BaselineEntry> current)
    {
        var result = new List<BaselineEntry>(existing);
        var existingKeys = new HashSet<string>(existing.Select(ToKey));

        foreach (var entry in current)
        {
            var key = ToKey(entry);
            if (!existingKeys.Contains(key))
            {
                result.Add(entry);
                existingKeys.Add(key);
            }
        }

        return result;
    }
}
