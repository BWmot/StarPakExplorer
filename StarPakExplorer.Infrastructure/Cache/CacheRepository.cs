using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Infrastructure.Cache;

public sealed class CacheRepository : ICacheRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CacheRepository()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        CacheRoot = Path.Combine(localAppData, "StarPakExplorer", "Cache");
        Directory.CreateDirectory(CacheRoot);
    }

    public string CacheRoot { get; }

    public string GetCacheKey(string pakPath)
    {
        var info = new FileInfo(pakPath);
        var source = string.Join("|",
            info.FullName.ToLowerInvariant(),
            info.Length,
            info.LastWriteTimeUtc.Ticks);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..32];
    }

    public string GetCacheDirectory(string cacheKey)
    {
        return Path.Combine(CacheRoot, cacheKey);
    }

    public string GetUnpackedDirectory(string cacheKey)
    {
        return Path.Combine(GetCacheDirectory(cacheKey), "unpacked");
    }

    public async Task<PakManifest?> TryLoadManifestAsync(string cacheKey, CancellationToken cancellationToken)
    {
        var manifestPath = GetManifestPath(cacheKey);
        var unpackedPath = GetUnpackedDirectory(cacheKey);
        if (!File.Exists(manifestPath) || !Directory.Exists(unpackedPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(manifestPath);
        return await JsonSerializer.DeserializeAsync<PakManifest>(stream, JsonOptions, cancellationToken);
    }

    public async Task<PakManifest?> TryLoadManifestByPakPathAsync(string pakPath, CancellationToken cancellationToken)
    {
        var cacheKey = GetCacheKey(pakPath);
        return await TryLoadManifestAsync(cacheKey, cancellationToken);
    }

    public Task PrepareFreshCacheAsync(string cacheKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cacheDirectory = GetCacheDirectory(cacheKey);
        if (Directory.Exists(cacheDirectory))
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }

        Directory.CreateDirectory(GetUnpackedDirectory(cacheKey));
        return Task.CompletedTask;
    }

    public async Task SaveManifestAsync(PakManifest manifest, CancellationToken cancellationToken)
    {
        var cacheDirectory = GetCacheDirectory(manifest.CacheKey);
        Directory.CreateDirectory(cacheDirectory);

        var manifestPath = GetManifestPath(manifest.CacheKey);
        await using var stream = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
    }

    public Task DeleteAsync(string cacheKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cacheDirectory = GetCacheDirectory(cacheKey);
        if (Directory.Exists(cacheDirectory))
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Directory.Exists(CacheRoot))
        {
            Directory.Delete(CacheRoot, recursive: true);
        }

        Directory.CreateDirectory(CacheRoot);
        return Task.CompletedTask;
    }

    public Task<CacheOverview> GetOverviewAsync(int maxEntries, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(CacheRoot))
        {
            return Task.FromResult(new CacheOverview());
        }

        var entries = new List<CacheEntrySummary>();
        long totalBytes = 0;

        foreach (var manifestPath in Directory.EnumerateFiles(CacheRoot, "manifest.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var cacheDirectory = Path.GetDirectoryName(manifestPath) ?? CacheRoot;
                var unpackedDirectory = Path.Combine(cacheDirectory, "unpacked");
                if (!Directory.Exists(unpackedDirectory))
                {
                    continue;
                }

                var manifest = ReadManifest(manifestPath);
                if (manifest is null)
                {
                    continue;
                }

                var cacheBytes = GetDirectorySize(cacheDirectory);
                totalBytes += cacheBytes;
                entries.Add(new CacheEntrySummary
                {
                    CacheKey = manifest.CacheKey,
                    PakPath = manifest.PakPath,
                    ModName = manifest.ModName,
                    WorkshopId = manifest.WorkshopId,
                    CreatedAt = manifest.CreatedAt,
                    PakSize = manifest.PakSize,
                    CacheBytes = cacheBytes
                });
            }
            catch
            {
                continue;
            }
        }

        var recent = entries
            .OrderByDescending(item => item.CreatedAt)
            .Take(Math.Max(0, maxEntries))
            .ToList();

        return Task.FromResult(new CacheOverview
        {
            TotalBytes = totalBytes,
            EntryCount = entries.Count,
            RecentEntries = recent
        });
    }

    private string GetManifestPath(string cacheKey)
    {
        return Path.Combine(GetCacheDirectory(cacheKey), "manifest.json");
    }

    private static PakManifest? ReadManifest(string manifestPath)
    {
        using var stream = File.OpenRead(manifestPath);
        return JsonSerializer.Deserialize<PakManifest>(stream, JsonOptions);
    }

    private static long GetDirectorySize(string directoryPath)
    {
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
                // ignore inaccessible file
            }
        }

        return total;
    }
}
