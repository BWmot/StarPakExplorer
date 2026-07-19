using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Abstractions;

/// <summary>
/// Stores and manages a global, project-independent glossary that persists
/// translated terms across all translation projects.
/// </summary>
public interface IGlobalGlossaryStore
{
    /// <summary>Path to the global glossary JSON file.</summary>
    string GlossaryFilePath { get; }

    /// <summary>Load all entries from the global glossary.</summary>
    Task<IReadOnlyList<TranslationGlossaryEntry>> LoadAllAsync(CancellationToken cancellationToken);

    /// <summary>Save the full glossary (replaces all entries).</summary>
    Task SaveAllAsync(IReadOnlyList<TranslationGlossaryEntry> entries, CancellationToken cancellationToken);

    /// <summary>Add or update a single entry (keyed by Source). Saves immediately.</summary>
    Task UpsertAsync(TranslationGlossaryEntry entry, CancellationToken cancellationToken);

    /// <summary>Remove an entry by its Source text.</summary>
    Task<bool> DeleteAsync(string source, CancellationToken cancellationToken);

    /// <summary>
    /// Import entries from a tab-separated term bank file.
    /// Format: <c>English|||Chinese</c> or <c>Chinese|||English</c> (one per line).
    /// </summary>
    Task<int> ImportFromFileAsync(string filePath, CancellationToken cancellationToken);

    /// <summary>
    /// Export all entries to a tab-separated term bank file.
    /// </summary>
    Task ExportToFileAsync(string filePath, CancellationToken cancellationToken);

    /// <summary>Get a lookup dictionary (Source → Target) for use by translation engines.</summary>
    Task<Dictionary<string, string>> BuildLookupAsync(CancellationToken cancellationToken);
}
