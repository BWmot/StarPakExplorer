using System.Text;

namespace StarPakExplorer.Application.Abstractions;

public interface IFileStagingStore
{
    string GetStagingRoot(string cacheKey);

    string GetStagedFilePath(string cacheKey, string relativePath);

    Task SaveTextAsync(
        string cacheKey,
        string relativePath,
        string content,
        Encoding encoding,
        CancellationToken cancellationToken);

    Task SaveReplacementAsync(
        string cacheKey,
        string relativePath,
        string replacementPath,
        CancellationToken cancellationToken);

    Task RemoveAsync(string cacheKey, string relativePath, CancellationToken cancellationToken);
}
