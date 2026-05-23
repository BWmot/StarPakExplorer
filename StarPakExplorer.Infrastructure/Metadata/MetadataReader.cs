using System.Text.Json;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Infrastructure.Metadata;

public sealed class MetadataReader : IMetadataReader
{
    private readonly IAppLogger logger;

    public MetadataReader(IAppLogger logger)
    {
        this.logger = logger;
    }

    public async Task<ModMetadata> ReadAsync(
        string unpackedDirectory,
        string? workshopId,
        CancellationToken cancellationToken)
    {
        var metadataPath = FindMetadataFile(unpackedDirectory);
        if (metadataPath is null)
        {
            return new ModMetadata { SteamContentId = workshopId };
        }

        try
        {
            var text = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            var root = document.RootElement;
            return new ModMetadata
            {
                Name = ReadString(root, "name"),
                FriendlyName = ReadString(root, "friendlyName"),
                Author = ReadString(root, "author"),
                SteamContentId = ReadString(root, "steamContentId") ?? workshopId
            };
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.Error($"Failed to read metadata: {metadataPath}", exception);
            return new ModMetadata { SteamContentId = workshopId };
        }
    }

    private static string? FindMetadataFile(string unpackedDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(unpackedDirectory, "_metadata"),
            Path.Combine(unpackedDirectory, ".metadata")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }
}
