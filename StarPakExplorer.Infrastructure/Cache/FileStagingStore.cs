using System.Text;
using StarPakExplorer.Application.Abstractions;

namespace StarPakExplorer.Infrastructure.Cache;

public sealed class FileStagingStore : IFileStagingStore
{
    private readonly ICacheRepository cacheRepository;

    public FileStagingStore(ICacheRepository cacheRepository)
    {
        this.cacheRepository = cacheRepository;
    }

    public string GetStagingRoot(string cacheKey)
    {
        return Path.Combine(cacheRepository.GetCacheDirectory(cacheKey), "staging");
    }

    public string GetStagedFilePath(string cacheKey, string relativePath)
    {
        return Path.Combine(GetStagingRoot(cacheKey), relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public async Task SaveTextAsync(
        string cacheKey,
        string relativePath,
        string content,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        var stagedPath = GetStagedFilePath(cacheKey, relativePath);
        var directory = Path.GetDirectoryName(stagedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(stagedPath, content, encoding, cancellationToken);
    }

    public async Task SaveReplacementAsync(
        string cacheKey,
        string relativePath,
        string replacementPath,
        CancellationToken cancellationToken)
    {
        var stagedPath = GetStagedFilePath(cacheKey, relativePath);
        var directory = Path.GetDirectoryName(stagedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await using var source = File.OpenRead(replacementPath);
        await using var destination = File.Create(stagedPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    public Task RemoveAsync(string cacheKey, string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stagedPath = GetStagedFilePath(cacheKey, relativePath);
        if (File.Exists(stagedPath))
        {
            File.Delete(stagedPath);
        }

        return Task.CompletedTask;
    }
}
