using System.Text;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Infrastructure.Files;

public sealed class TextFileReader : ITextFileReader
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".bmp",
        ".tif",
        ".tiff"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".item",
        ".activeitem",
        ".object",
        ".recipe",
        ".patch",
        ".config",
        ".lua",
        ".json",
        ".metadata",
        ".matitem",
        ".material",
        ".frames",
        ".animation",
        ".species",
        ".codex",
        ".questtemplate",
        ".tech",
        ".statuseffect",
        ".projectile",
        ".monsterpart",
        ".monstertype",
        ".npctype",
        ".behavior",
        ".biome",
        ".surfacebiome",
        ".undergroundbiome",
        ".dungeon",
        ".structure",
        ".treasurepools"
    };

    public bool IsPreviewSupported(ResourceFileRecord file)
    {
        return IsKnownText(file) || IsImagePreviewSupported(file);
    }

    public bool IsImagePreviewSupported(ResourceFileRecord file)
    {
        return ImageExtensions.Contains(file.Extension);
    }

    public bool IsSearchSupported(ResourceFileRecord file)
    {
        return IsKnownText(file);
    }

    public async Task<TextReadResult> ReadTextAsync(
        string fullPath,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(fullPath);
        var bytesToRead = (int)Math.Min(info.Length, maxBytes);
        var buffer = new byte[bytesToRead];

        await using var stream = File.OpenRead(fullPath);
        var read = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);
        var content = Encoding.UTF8.GetString(buffer, 0, read);

        return new TextReadResult
        {
            Content = content,
            WasTruncated = info.Length > maxBytes
        };
    }

    public async Task<byte[]> ReadBinaryAsync(
        string fullPath,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(fullPath);
        var bytesToRead = (int)Math.Min(info.Length, maxBytes);
        var buffer = new byte[bytesToRead];

        await using var stream = File.OpenRead(fullPath);
        var read = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);
        if (read == buffer.Length)
        {
            return buffer;
        }

        return buffer[..read];
    }

    private static bool IsKnownText(ResourceFileRecord file)
    {
        var fileName = Path.GetFileName(file.RelativePath);
        return TextExtensions.Contains(file.Extension)
            || string.Equals(fileName, "_metadata", StringComparison.OrdinalIgnoreCase);
    }
}
