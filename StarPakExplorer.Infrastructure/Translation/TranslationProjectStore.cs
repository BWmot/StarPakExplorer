using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Infrastructure.Translation;

public sealed class TranslationProjectStore : ITranslationProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppSettings appSettings;

    public TranslationProjectStore(AppSettings appSettings)
    {
        this.appSettings = appSettings;
    }

    public string GetTranslationRoot()
    {
        var configuredRoot = appSettings.TranslationRootDirectory;
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarPakExplorer", "Translations")
            : configuredRoot;

        Directory.CreateDirectory(root);
        return root;
    }

    public string GetProjectKey(PakManifest manifest)
    {
        var sourceName = !string.IsNullOrWhiteSpace(manifest.ModName)
            ? manifest.ModName!
            : Path.GetFileNameWithoutExtension(manifest.PakPath);

        return $"CN_{SanitizeSegment(sourceName)}_zhCN";
    }

    public string GetProjectDirectory(string projectKey)
    {
        return Path.Combine(GetTranslationRoot(), projectKey);
    }

    public string GetProgressPath(string projectKey)
    {
        return Path.Combine(GetProjectDirectory(projectKey), "translation_progress.json");
    }

    public string GetGlossaryPath(string projectKey)
    {
        return Path.Combine(GetProjectDirectory(projectKey), "glossary.json");
    }

    public string GetCacheDirectory(string projectKey)
    {
        return Path.Combine(GetProjectDirectory(projectKey), "cache");
    }

    public string GetTranslationsCachePath(string projectKey)
    {
        return Path.Combine(GetCacheDirectory(projectKey), "translations_cache.json");
    }

    public string GetFileTranslationsPath(string projectKey)
    {
        return Path.Combine(GetCacheDirectory(projectKey), "file_translations.json");
    }

    public async Task<TranslationProgressDocument?> LoadProgressAsync(string projectKey, CancellationToken cancellationToken)
    {
        var path = GetProgressPath(projectKey);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<TranslationProgressDocument>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveProgressAsync(TranslationProgressDocument document, CancellationToken cancellationToken)
    {
        var directory = GetProjectDirectory(document.ProjectKey);
        Directory.CreateDirectory(directory);

        await using var stream = File.Create(GetProgressPath(document.ProjectKey));
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, string>> LoadTranslationsCacheAsync(string projectKey, CancellationToken cancellationToken)
    {
        var path = GetTranslationsCachePath(projectKey);
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? [];
    }

    public async Task SaveTranslationsCacheAsync(string projectKey, IDictionary<string, string> cache, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(GetCacheDirectory(projectKey));
        await using var stream = File.Create(GetTranslationsCachePath(projectKey));
        await JsonSerializer.SerializeAsync(stream, cache, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, Dictionary<string, string>>> LoadFileTranslationsAsync(string projectKey, CancellationToken cancellationToken)
    {
        var path = GetFileTranslationsPath(projectKey);
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, Dictionary<string, string>>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? [];
    }

    public async Task SaveFileTranslationsAsync(string projectKey, IDictionary<string, IDictionary<string, string>> cache, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(GetCacheDirectory(projectKey));

        var serializable = cache.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToDictionary(inner => inner.Key, inner => inner.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        await using var stream = File.Create(GetFileTranslationsPath(projectKey));
        await JsonSerializer.SerializeAsync(stream, serializable, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, string>> LoadGlossaryAsync(string projectKey, CancellationToken cancellationToken)
    {
        var path = GetGlossaryPath(projectKey);
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? [];
    }

    public async Task SaveGlossaryAsync(string projectKey, IDictionary<string, string> glossary, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(GetProjectDirectory(projectKey));
        await using var stream = File.Create(GetGlossaryPath(projectKey));
        await JsonSerializer.SerializeAsync(stream, glossary, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var result = builder.ToString().Trim('.');
        return string.IsNullOrWhiteSpace(result) ? "patch" : result;
    }
}
