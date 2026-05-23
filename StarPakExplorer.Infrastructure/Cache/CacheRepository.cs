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

    private string GetManifestPath(string cacheKey)
    {
        return Path.Combine(GetCacheDirectory(cacheKey), "manifest.json");
    }
}
