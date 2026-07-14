using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Abstractions;

public interface ITranslationProjectStore
{
    string GetTranslationRoot();

    string GetProjectKey(PakManifest manifest);

    string GetProjectDirectory(string projectKey);

    string GetProgressPath(string projectKey);

    string GetGlossaryPath(string projectKey);

    string GetCacheDirectory(string projectKey);

    string GetTranslationsCachePath(string projectKey);

    string GetFileTranslationsPath(string projectKey);

    Task<TranslationProgressDocument?> LoadProgressAsync(string projectKey, CancellationToken cancellationToken);

    Task SaveProgressAsync(TranslationProgressDocument document, CancellationToken cancellationToken);

    Task<Dictionary<string, string>> LoadTranslationsCacheAsync(string projectKey, CancellationToken cancellationToken);

    Task SaveTranslationsCacheAsync(string projectKey, IDictionary<string, string> cache, CancellationToken cancellationToken);

    Task<Dictionary<string, Dictionary<string, string>>> LoadFileTranslationsAsync(string projectKey, CancellationToken cancellationToken);

    Task SaveFileTranslationsAsync(string projectKey, IDictionary<string, IDictionary<string, string>> cache, CancellationToken cancellationToken);

    Task<Dictionary<string, string>> LoadGlossaryAsync(string projectKey, CancellationToken cancellationToken);

    Task SaveGlossaryAsync(string projectKey, IDictionary<string, string> glossary, CancellationToken cancellationToken);
}
