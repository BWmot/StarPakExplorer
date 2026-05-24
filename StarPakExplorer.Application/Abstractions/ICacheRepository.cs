using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Abstractions;

public interface ICacheRepository
{
    string CacheRoot { get; }

    string GetCacheKey(string pakPath);

    string GetCacheDirectory(string cacheKey);

    string GetUnpackedDirectory(string cacheKey);

    Task<PakManifest?> TryLoadManifestAsync(string cacheKey, CancellationToken cancellationToken);

    Task PrepareFreshCacheAsync(string cacheKey, CancellationToken cancellationToken);

    Task SaveManifestAsync(PakManifest manifest, CancellationToken cancellationToken);

    Task DeleteAsync(string cacheKey, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);

    Task<CacheOverview> GetOverviewAsync(int maxEntries, CancellationToken cancellationToken);

    Task<PakManifest?> TryLoadManifestByPakPathAsync(string pakPath, CancellationToken cancellationToken);
}
