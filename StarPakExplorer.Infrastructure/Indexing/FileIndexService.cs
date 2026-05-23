using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Infrastructure.Indexing;

public sealed class FileIndexService : IFileIndexService
{
    public IReadOnlyList<ResourceFileRecord> BuildIndex(string unpackedDirectory)
    {
        if (!Directory.Exists(unpackedDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(unpackedDirectory, "*", SearchOption.AllDirectories)
            .Select(path => CreateRecord(unpackedDirectory, path))
            .OrderBy(record => record.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ResourceFileRecord CreateRecord(string root, string fullPath)
    {
        var relativePath = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        return new ResourceFileRecord
        {
            RelativePath = relativePath,
            FullPath = fullPath,
            Extension = Path.GetExtension(fullPath),
            SizeBytes = new FileInfo(fullPath).Length
        };
    }
}
