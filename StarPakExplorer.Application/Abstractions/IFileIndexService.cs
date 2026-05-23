using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Abstractions;

public interface IFileIndexService
{
    IReadOnlyList<ResourceFileRecord> BuildIndex(string unpackedDirectory);
}
