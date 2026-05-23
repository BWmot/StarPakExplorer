using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Abstractions;

public interface ITextFileReader
{
    bool IsPreviewSupported(ResourceFileRecord file);

    bool IsImagePreviewSupported(ResourceFileRecord file);

    bool IsSearchSupported(ResourceFileRecord file);

    Task<TextReadResult> ReadTextAsync(string fullPath, int maxBytes, CancellationToken cancellationToken);

    Task<byte[]> ReadBinaryAsync(string fullPath, int maxBytes, CancellationToken cancellationToken);
}
