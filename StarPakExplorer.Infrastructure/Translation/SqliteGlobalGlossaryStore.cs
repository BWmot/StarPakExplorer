using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Infrastructure.Translation;

/// <summary>
/// SQLite-based global glossary store. The glossary survives across all
/// translation projects and can be browsed, searched, imported, exported and
/// edited from the in-app glossary window.
///
/// On first use, a legacy <c>global_glossary.json</c> file (if present) is
/// automatically migrated into the SQLite database and renamed with a
/// <c>.migrated</c> suffix.
/// </summary>
public sealed class SqliteGlobalGlossaryStore : IGlobalGlossaryStore
{
    private const string DefaultDbFileName = "global_glossary.db";
    private const string DefaultLegacyJsonFileName = "global_glossary.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly DateTimeOffset UnixEpoch = DateTimeOffset.FromUnixTimeSeconds(0);

    // Decodes JSON-style "\uXXXX" escape sequences (e.g. "\u57C3\u6C83\u5229\u7279" -> "埃沃利特")
    // that may appear literally inside term-bank import files. Surrogate pairs for astral
    // characters are handled naturally: two consecutive matches produce the two UTF-16 units.
    private static readonly Regex UnicodeEscapeRegex = new(@"\\u([0-9a-fA-F]{4})", RegexOptions.Compiled);

    private readonly string dbFilePath;
    private readonly string? legacyJsonPath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;

    public SqliteGlobalGlossaryStore(AppSettings appSettings)
    {
        var configured = appSettings.GlobalGlossaryPath?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(configured))
        {
            dbFilePath = Path.Combine(AppContext.BaseDirectory, DefaultDbFileName);
            legacyJsonPath = Path.Combine(AppContext.BaseDirectory, DefaultLegacyJsonFileName);
        }
        else if (string.Equals(Path.GetExtension(configured), ".json", StringComparison.OrdinalIgnoreCase))
        {
            // User pointed at the legacy JSON file → derive the DB path next to it and migrate.
            dbFilePath = Path.ChangeExtension(configured, ".db");
            legacyJsonPath = configured;
        }
        else
        {
            dbFilePath = configured;
            legacyJsonPath = null;
        }

        var directory = Path.GetDirectoryName(dbFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public string GlossaryFilePath => dbFilePath;

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await InitializeIfNeededAsync(cancellationToken).ConfigureAwait(false);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM glossary;";
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<TranslationGlossaryEntry>> LoadAllAsync(CancellationToken cancellationToken)
    {
        await InitializeIfNeededAsync(cancellationToken).ConfigureAwait(false);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            return await ReadEntriesAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<TranslationGlossaryEntry>> SearchAsync(
        string? keyword,
        string? language,
        int limit,
        CancellationToken cancellationToken)
    {
        await InitializeIfNeededAsync(cancellationToken).ConfigureAwait(false);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            return await ReadEntriesAsync(connection, keyword, language, limit, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAllAsync(IReadOnlyList<TranslationGlossaryEntry> entries, CancellationToken cancellationToken)
    {
        await InitializeIfNeededAsync(cancellationToken).ConfigureAwait(false);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await using var transaction = connection.BeginTransaction();
            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM glossary;";
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await InsertIgnoreManyAsync(connection, transaction, entries, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpsertAsync(TranslationGlossaryEntry entry, CancellationToken cancellationToken)
    {
        await UpsertManyAsync([entry], cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> UpsertManyAsync(IReadOnlyList<TranslationGlossaryEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return 0;
        }

        await InitializeIfNeededAsync(cancellationToken).ConfigureAwait(false);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await using var transaction = connection.BeginTransaction();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO glossary (source, language, target, entry_source, category, notes, modified_at)
                VALUES ($source, $language, $target, $entrySource, $category, $notes, $modifiedAt)
                ON CONFLICT(source, language) DO UPDATE SET
                    target = excluded.target,
                    entry_source = excluded.entry_source,
                    category = excluded.category,
                    notes = excluded.notes,
                    modified_at = excluded.modified_at;
                """;
            var pSource = command.Parameters.Add("$source", SqliteType.Text);
            var pLanguage = command.Parameters.Add("$language", SqliteType.Text);
            var pTarget = command.Parameters.Add("$target", SqliteType.Text);
            var pEntrySource = command.Parameters.Add("$entrySource", SqliteType.Integer);
            var pCategory = command.Parameters.Add("$category", SqliteType.Text);
            var pNotes = command.Parameters.Add("$notes", SqliteType.Text);
            var pModifiedAt = command.Parameters.Add("$modifiedAt", SqliteType.Text);

            int changed = 0;
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Source))
                {
                    continue;
                }

                BindEntry(command, pSource, pLanguage, pTarget, pEntrySource, pCategory, pNotes, pModifiedAt, entry);
                changed += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return changed;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string source, string? language, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return await DeleteManyAsync([source], cancellationToken).ConfigureAwait(false) > 0;
        }

        await InitializeIfNeededAsync(cancellationToken).ConfigureAwait(false);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM glossary WHERE source = $source AND language = $language COLLATE NOCASE;";
            command.Parameters.Add("$source", SqliteType.Text).Value = source;
            command.Parameters.Add("$language", SqliteType.Text).Value = language;
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<int> DeleteManyAsync(IReadOnlyCollection<string> sources, CancellationToken cancellationToken)
    {
        if (sources.Count == 0)
        {
            return 0;
        }

        await InitializeIfNeededAsync(cancellationToken).ConfigureAwait(false);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await using var transaction = connection.BeginTransaction();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM glossary WHERE source = $source;";
            var pSource = command.Parameters.Add("$source", SqliteType.Text);

            int removed = 0;
            foreach (var source in sources)
            {
                if (string.IsNullOrWhiteSpace(source))
                {
                    continue;
                }

                pSource.Value = source;
                removed += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return removed;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string DecodeUnicodeEscapes(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf("\\u", StringComparison.Ordinal) < 0)
        {
            return value ?? string.Empty;
        }

        return UnicodeEscapeRegex.Replace(value, m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
    }

    public async Task<int> ImportFromFileAsync(string filePath, string? language, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false);

        var fallbackLanguage = string.IsNullOrWhiteSpace(language) ? "zh-CN" : language.Trim();
        var imported = new List<TranslationGlossaryEntry>(lines.Length);
        var now = DateTimeOffset.Now;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            // Format: English|||Chinese  OR  Chinese|||English  OR  source|||target|||language
            var parts = trimmed.Split("|||", StringSplitOptions.None);
            if (parts.Length < 2)
            {
                continue;
            }

            string source, target, entryLanguage;
            if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                // Explicit three-field line: source|||target|||language
                source = DecodeUnicodeEscapes(parts[0].Trim());
                target = DecodeUnicodeEscapes(parts[1].Trim());
                entryLanguage = DecodeUnicodeEscapes(parts[2].Trim());
            }
            else if (IsLikelyEnglish(parts[0]))
            {
                source = DecodeUnicodeEscapes(parts[0].Trim());
                target = DecodeUnicodeEscapes(parts[1].Trim());
                entryLanguage = fallbackLanguage;
            }
            else
            {
                source = DecodeUnicodeEscapes(parts[1].Trim());
                target = DecodeUnicodeEscapes(parts[0].Trim());
                entryLanguage = fallbackLanguage;
            }

            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(entryLanguage))
            {
                continue;
            }

            imported.Add(new TranslationGlossaryEntry
            {
                Source = source,
                Target = target,
                Language = entryLanguage,
                EntrySource = GlossaryEntrySource.Imported,
                ModifiedAt = now
            });
        }

        if (imported.Count == 0)
        {
            return 0;
        }

        // INSERT OR IGNORE keeps existing entries untouched (matches the legacy JSON behaviour).
        await InitializeIfNeededAsync(cancellationToken).ConfigureAwait(false);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await using var transaction = connection.BeginTransaction();
            var inserted = await InsertIgnoreManyAsync(connection, transaction, imported, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return inserted;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ExportToFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var entries = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var lines = entries
            .OrderBy(e => e.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Language, StringComparer.OrdinalIgnoreCase)
            .Select(e => $"{e.Source}|||{e.Target}|||{e.Language}");

        await File.WriteAllLinesAsync(filePath, lines, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, string>> BuildLookupAsync(string language, CancellationToken cancellationToken)
    {
        var entries = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return entries
            .Where(e => string.Equals(e.Language, language, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(e.Source)
                        && !string.IsNullOrWhiteSpace(e.Target))
            .ToDictionary(e => e.Source, e => e.Target, StringComparer.OrdinalIgnoreCase);
    }

    private async Task InitializeIfNeededAsync(CancellationToken cancellationToken)
    {
        if (initialized)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized)
            {
                return;
            }

            await using var connection = CreateConnection();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    PRAGMA journal_mode = WAL;
                    CREATE TABLE IF NOT EXISTS glossary (
                        source TEXT NOT NULL COLLATE NOCASE,
                        language TEXT NOT NULL COLLATE NOCASE DEFAULT 'zh-CN',
                        target TEXT NOT NULL,
                        entry_source INTEGER NOT NULL DEFAULT 0,
                        category TEXT NULL,
                        notes TEXT NULL,
                        modified_at TEXT NOT NULL,
                        PRIMARY KEY (source, language)
                    );
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // If the table already existed with the old single-language schema,
            // rebuild it to add the language column before creating the index.
            await MigrateSchemaIfNeededAsync(connection, cancellationToken).ConfigureAwait(false);

            await using (var indexCommand = connection.CreateCommand())
            {
                indexCommand.CommandText = """
                    CREATE INDEX IF NOT EXISTS ix_glossary_target ON glossary(target);
                    CREATE INDEX IF NOT EXISTS ix_glossary_language ON glossary(language);
                    """;
                await indexCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            if (legacyJsonPath is not null && File.Exists(legacyJsonPath))
            {
                await MigrateFromJsonAsync(connection, cancellationToken).ConfigureAwait(false);
            }

            initialized = true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Upgrades a database created by the earlier single-language schema
    /// (PRIMARY KEY on source only, no language column) to the multi-language
    /// schema (PRIMARY KEY on source + language). Existing rows become
    /// "zh-CN" entries. Runs only when the table lacks a language column.
    /// </summary>
    private static async Task MigrateSchemaIfNeededAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        bool hasLanguageColumn;
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(glossary);";
            await using var reader = await pragma.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            hasLanguageColumn = false;
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(reader.GetString(1), "language", StringComparison.OrdinalIgnoreCase))
                {
                    hasLanguageColumn = true;
                    break;
                }
            }
        }

        if (hasLanguageColumn)
        {
            return;
        }

        await using var transaction = connection.BeginTransaction();
        try
        {
            await using (var migrate = connection.CreateCommand())
            {
                migrate.Transaction = transaction;
                migrate.CommandText = """
                    CREATE TABLE glossary_new (
                        source TEXT NOT NULL COLLATE NOCASE,
                        language TEXT NOT NULL COLLATE NOCASE DEFAULT 'zh-CN',
                        target TEXT NOT NULL,
                        entry_source INTEGER NOT NULL DEFAULT 0,
                        category TEXT NULL,
                        notes TEXT NULL,
                        modified_at TEXT NOT NULL,
                        PRIMARY KEY (source, language)
                    );
                    INSERT INTO glossary_new (source, language, target, entry_source, category, notes, modified_at)
                        SELECT source, 'zh-CN', target, entry_source, category, notes, modified_at FROM glossary;
                    DROP TABLE glossary;
                    ALTER TABLE glossary_new RENAME TO glossary;
                    CREATE INDEX IF NOT EXISTS ix_glossary_target ON glossary(target);
                    CREATE INDEX IF NOT EXISTS ix_glossary_language ON glossary(language);
                    """;
                await migrate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task MigrateFromJsonAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // Migrate once: a .migrated backup marks this JSON as already consumed.
        // We deliberately do NOT gate on the row count — the startup term-bank
        // auto-import may have populated the DB already, and skipping on count>0
        // would silently drop JSON entries that the term-bank file lacks.
        var backup = legacyJsonPath + ".migrated";
        if (File.Exists(backup))
        {
            return;
        }

        List<TranslationGlossaryEntry> entries;
        await using (var stream = File.OpenRead(legacyJsonPath!))
        {
            entries = await JsonSerializer.DeserializeAsync<List<TranslationGlossaryEntry>>(
                stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
        }

        if (entries.Count == 0)
        {
            return;
        }

        // INSERT OR IGNORE keeps any existing (source, language) rows and only
        // adds missing ones, so a pre-populated database is never clobbered.
        await using var transaction = connection.BeginTransaction();
        await InsertIgnoreManyAsync(connection, transaction, entries, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // Preserve the migrated file rather than deleting it.
        try
        {
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }

            File.Move(legacyJsonPath!, backup);
        }
        catch (Exception)
        {
            // Non-fatal: the data has already been copied into SQLite.
        }
    }

    private static async Task<int> InsertIgnoreManyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<TranslationGlossaryEntry> entries,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO glossary (source, language, target, entry_source, category, notes, modified_at)
            VALUES ($source, $language, $target, $entrySource, $category, $notes, $modifiedAt);
            """;
        var pSource = command.Parameters.Add("$source", SqliteType.Text);
        var pLanguage = command.Parameters.Add("$language", SqliteType.Text);
        var pTarget = command.Parameters.Add("$target", SqliteType.Text);
        var pEntrySource = command.Parameters.Add("$entrySource", SqliteType.Integer);
        var pCategory = command.Parameters.Add("$category", SqliteType.Text);
        var pNotes = command.Parameters.Add("$notes", SqliteType.Text);
        var pModifiedAt = command.Parameters.Add("$modifiedAt", SqliteType.Text);

        int inserted = 0;
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Source) || string.IsNullOrWhiteSpace(entry.Target))
            {
                continue;
            }

            BindEntry(command, pSource, pLanguage, pTarget, pEntrySource, pCategory, pNotes, pModifiedAt, entry);
            inserted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return inserted;
    }

    private static void BindEntry(
        SqliteCommand command,
        SqliteParameter pSource,
        SqliteParameter pLanguage,
        SqliteParameter pTarget,
        SqliteParameter pEntrySource,
        SqliteParameter pCategory,
        SqliteParameter pNotes,
        SqliteParameter pModifiedAt,
        TranslationGlossaryEntry entry)
    {
        pSource.Value = entry.Source;
        pLanguage.Value = string.IsNullOrWhiteSpace(entry.Language) ? "zh-CN" : entry.Language.Trim();
        pTarget.Value = entry.Target ?? "";
        pEntrySource.Value = (int)entry.EntrySource;
        pCategory.Value = string.IsNullOrWhiteSpace(entry.Category) ? DBNull.Value : entry.Category;
        pNotes.Value = string.IsNullOrWhiteSpace(entry.Notes) ? DBNull.Value : entry.Notes;
        pModifiedAt.Value = FormatTimestamp(entry.ModifiedAt);
    }

    private static async Task<IReadOnlyList<TranslationGlossaryEntry>> ReadEntriesAsync(
        SqliteConnection connection,
        string? keyword,
        string? language,
        int limit,
        CancellationToken cancellationToken)
    {
        var entries = new List<TranslationGlossaryEntry>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source, language, target, entry_source, category, notes, modified_at
            FROM glossary
            WHERE ($language IS NULL OR language = $language COLLATE NOCASE)
              AND ($keyword IS NULL
                   OR source LIKE $pattern ESCAPE '\'
                   OR target LIKE $pattern ESCAPE '\'
                   OR category LIKE $pattern ESCAPE '\'
                   OR notes LIKE $pattern ESCAPE '\')
            ORDER BY source COLLATE NOCASE, language COLLATE NOCASE
            LIMIT $limit;
            """;

        var languageParam = command.Parameters.Add("$language", SqliteType.Text);
        var keywordParam = command.Parameters.Add("$keyword", SqliteType.Text);
        var patternParam = command.Parameters.Add("$pattern", SqliteType.Text);
        var limitParam = command.Parameters.Add("$limit", SqliteType.Integer);

        language = language?.Trim();
        languageParam.Value = string.IsNullOrWhiteSpace(language) ? DBNull.Value : language;
        keyword = keyword?.Trim();
        keywordParam.Value = string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : keyword;
        patternParam.Value = "%" + EscapeLike(keyword ?? "") + "%";
        limitParam.Value = limit;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
    }

    private static async Task<IReadOnlyList<TranslationGlossaryEntry>> ReadEntriesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var entries = new List<TranslationGlossaryEntry>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source, language, target, entry_source, category, notes, modified_at
            FROM glossary
            ORDER BY source COLLATE NOCASE, language COLLATE NOCASE;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
    }

    private static TranslationGlossaryEntry ReadEntry(SqliteDataReader reader)
    {
        return new TranslationGlossaryEntry
        {
            Source = reader.GetString(0),
            Language = reader.GetString(1),
            Target = reader.GetString(2),
            EntrySource = (GlossaryEntrySource)reader.GetInt32(3),
            Category = reader.IsDBNull(4) ? null : reader.GetString(4),
            Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
            ModifiedAt = ParseTimestamp(reader.GetString(6))
        };
    }

    private SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection($"Data Source={dbFilePath}");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : UnixEpoch;
    }

    private static string EscapeLike(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    private static bool IsLikelyEnglish(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Heuristic: if the first non-punctuation character is an ASCII letter → likely English.
        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                return ch <= 127;
            }
        }

        return true; // Default assume English.
    }
}
