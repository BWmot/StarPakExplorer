using System.Text.Json;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Infrastructure.Translation;

/// <summary>
/// JSON-file-based global glossary store. The glossary survives across all
/// translation projects and can be manually edited or imported.
/// </summary>
public sealed class GlobalGlossaryStore : IGlobalGlossaryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string glossaryFilePath;
    private readonly SemaphoreSlim gate = new(1, 1);

    public GlobalGlossaryStore(AppSettings appSettings)
    {
        glossaryFilePath = string.IsNullOrWhiteSpace(appSettings.GlobalGlossaryPath)
            ? Path.Combine(
                AppContext.BaseDirectory,
                "global_glossary.json")
            : appSettings.GlobalGlossaryPath;

        // Ensure directory exists
        var directory = Path.GetDirectoryName(glossaryFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public string GlossaryFilePath => glossaryFilePath;

    public async Task<IReadOnlyList<TranslationGlossaryEntry>> LoadAllAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(glossaryFilePath))
            {
                return Array.Empty<TranslationGlossaryEntry>();
            }

            await using var stream = File.OpenRead(glossaryFilePath);
            var entries = await JsonSerializer.DeserializeAsync<List<TranslationGlossaryEntry>>(
                stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return entries ?? [];
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAllAsync(IReadOnlyList<TranslationGlossaryEntry> entries, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(glossaryFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(glossaryFilePath);
            await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpsertAsync(TranslationGlossaryEntry entry, CancellationToken cancellationToken)
    {
        var entries = (await LoadAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var existing = entries.FindIndex(e =>
            string.Equals(e.Source, entry.Source, StringComparison.OrdinalIgnoreCase));

        entry.ModifiedAt = DateTimeOffset.Now;

        if (existing >= 0)
        {
            entries[existing] = entry;
        }
        else
        {
            entries.Add(entry);
        }

        await SaveAllAsync(entries, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string source, CancellationToken cancellationToken)
    {
        var entries = (await LoadAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var removed = entries.RemoveAll(e =>
            string.Equals(e.Source, source, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
        {
            await SaveAllAsync(entries, cancellationToken).ConfigureAwait(false);
        }

        return removed > 0;
    }

    public async Task<int> ImportFromFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false);
        var existing = (await LoadAllAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(e => e.Source, StringComparer.OrdinalIgnoreCase);

        int imported = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            // Format: English|||Chinese  OR  Chinese|||English
            var parts = trimmed.Split("|||", StringSplitOptions.None);
            if (parts.Length < 2)
            {
                continue;
            }

            string source, target;

            // Detect direction: if the first part contains only ASCII, assume EN→ZH
            if (IsLikelyEnglish(parts[0]))
            {
                source = parts[0].Trim();
                target = parts[1].Trim();
            }
            else
            {
                source = parts[1].Trim();
                target = parts[0].Trim();
            }

            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            if (existing.ContainsKey(source))
            {
                continue; // Already present
            }

            existing[source] = new TranslationGlossaryEntry
            {
                Source = source,
                Target = target,
                EntrySource = GlossaryEntrySource.Imported,
                ModifiedAt = DateTimeOffset.Now
            };

            imported++;
        }

        if (imported > 0)
        {
            await SaveAllAsync(existing.Values.ToList(), cancellationToken).ConfigureAwait(false);
        }

        return imported;
    }

    public async Task ExportToFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var entries = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var lines = entries
            .OrderBy(e => e.Source, StringComparer.OrdinalIgnoreCase)
            .Select(e => $"{e.Source}|||{e.Target}");

        await File.WriteAllLinesAsync(filePath, lines, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, string>> BuildLookupAsync(CancellationToken cancellationToken)
    {
        var entries = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Source) && !string.IsNullOrWhiteSpace(e.Target))
            .ToDictionary(e => e.Source, e => e.Target, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsLikelyEnglish(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Heuristic: if the first non-punctuation character is ASCII letter → likely English
        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                return ch <= 127;
            }
        }

        return true; // Default assume English
    }
}
