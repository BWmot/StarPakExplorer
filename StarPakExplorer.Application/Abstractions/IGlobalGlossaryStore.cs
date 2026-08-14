using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Abstractions;

/// <summary>
/// Stores and manages a global, project-independent glossary that persists
/// translated terms across all translation projects.
///
/// Every entry is keyed by <c>(Source, Language)</c> so the same original term
/// can have translations in multiple target languages (e.g. zh-CN, zh-TW, ja,
/// ko, en). The default target language is "zh-CN".
/// </summary>
public interface IGlobalGlossaryStore
{
    /// <summary>Path to the global glossary database file (SQLite).</summary>
    string GlossaryFilePath { get; }

    /// <summary>Total number of entries currently stored (across all languages).</summary>
    Task<int> CountAsync(CancellationToken cancellationToken);

    /// <summary>Load all entries from the global glossary.</summary>
    Task<IReadOnlyList<TranslationGlossaryEntry>> LoadAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Search entries by keyword across Source, Target, Category and Notes.
    /// Returns up to <paramref name="limit"/> results ordered by Source.
    /// An empty/whitespace keyword returns all entries. Pass
    /// <paramref name="language"/> to restrict to one target language,
    /// or <c>null</c>/whitespace to search all languages.
    /// </summary>
    Task<IReadOnlyList<TranslationGlossaryEntry>> SearchAsync(
        string? keyword,
        string? language,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Save the full glossary (replaces all entries).</summary>
    Task SaveAllAsync(IReadOnlyList<TranslationGlossaryEntry> entries, CancellationToken cancellationToken);

    /// <summary>Add or update a single entry (keyed by Source + Language). Saves immediately.</summary>
    Task UpsertAsync(TranslationGlossaryEntry entry, CancellationToken cancellationToken);

    /// <summary>Add or update multiple entries in a single transaction (keyed by Source + Language).</summary>
    Task<int> UpsertManyAsync(IReadOnlyList<TranslationGlossaryEntry> entries, CancellationToken cancellationToken);

    /// <summary>
    /// Remove an entry. If <paramref name="language"/> is null/whitespace, all
    /// languages of that Source are removed; otherwise only that language row.
    /// </summary>
    Task<bool> DeleteAsync(string source, string? language, CancellationToken cancellationToken);

    /// <summary>Remove multiple entries by Source text in a single transaction (all languages). Returns the number removed.</summary>
    Task<int> DeleteManyAsync(IReadOnlyCollection<string> sources, CancellationToken cancellationToken);

    /// <summary>
    /// Import entries from a tab-separated term bank file.
    /// Format: <c>English|||Chinese</c> / <c>Chinese|||English</c> (one per line)
    /// or <c>source|||target|||language</c> for explicit language codes.
    /// Two-field lines are imported as <paramref name="language"/> (default "zh-CN").
    /// </summary>
    Task<int> ImportFromFileAsync(string filePath, string? language, CancellationToken cancellationToken);

    /// <summary>
    /// Export all entries to a tab-separated term bank file
    /// (<c>source|||target|||language</c> per line).
    /// </summary>
    Task ExportToFileAsync(string filePath, CancellationToken cancellationToken);

    /// <summary>
    /// Load all non-empty entries (Source → Target) restricted to one target
    /// language, for use by the translation service. Includes <see cref="TranslationGlossaryEntry.TermKind"/>
    /// so the service can compute the ambiguous-term set.
    /// </summary>
    Task<IReadOnlyList<TranslationGlossaryEntry>> LoadByLanguageAsync(string language, CancellationToken cancellationToken);
}
