using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Abstractions;

public interface IMetadataReader
{
    Task<ModMetadata> ReadAsync(string unpackedDirectory, string? workshopId, CancellationToken cancellationToken);
}
