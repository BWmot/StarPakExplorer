using System.Text;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Abstractions;

public interface IPatchStore
{
    string GetPatchRoot();

    string GetPatchKey(PakManifest manifest);

    string GetPatchSetDirectory(string patchKey);

    string GetPatchFilePath(string patchKey, string relativePath);

    Task EnsurePatchSetAsync(PakManifest manifest, CancellationToken cancellationToken);

    Task SaveTextAsync(
        string patchKey,
        string relativePath,
        string content,
        Encoding encoding,
        CancellationToken cancellationToken);

    Task SaveReplacementAsync(
        string patchKey,
        string relativePath,
        string replacementPath,
        CancellationToken cancellationToken);

    Task RemoveAsync(string patchKey, string relativePath, CancellationToken cancellationToken);

    Task<IReadOnlyList<PatchSetSummary>> GetPatchSetsAsync(int maxEntries, CancellationToken cancellationToken);

    Task<IReadOnlyList<PatchFileRecord>> GetFilesAsync(string patchKey, CancellationToken cancellationToken);

    Task DeletePatchSetAsync(string patchKey, CancellationToken cancellationToken);
}
