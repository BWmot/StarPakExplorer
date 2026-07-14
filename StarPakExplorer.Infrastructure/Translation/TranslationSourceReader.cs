using System.Text.Json;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Infrastructure.Translation;

public sealed class TranslationSourceReader : ITranslationSourceReader
{
    private static readonly HashSet<string> TranslatableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".item",
        ".activeitem",
        ".object",
        ".matitem",
        ".codex"
    };

    private static readonly HashSet<string> TranslatableFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "shortdescription",
        "description",
        "apexDescription",
        "avianDescription",
        "floranDescription",
        "glitchDescription",
        "humanDescription",
        "hylotlDescription",
        "novakidDescription",
        "feneroxDescription"
    };

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public Task<IReadOnlyList<TranslatableEntry>> ReadEntriesAsync(
        string unpackedModPath,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<TranslatableEntry>();
        ScanDirectory(unpackedModPath, unpackedModPath, entries, cancellationToken);
        return Task.FromResult<IReadOnlyList<TranslatableEntry>>(entries);
    }

    private static void ScanDirectory(
        string rootPath,
        string currentPath,
        List<TranslatableEntry> entries,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var filePath in Directory.EnumerateFiles(currentPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(filePath);
            if (!TranslatableExtensions.Contains(extension))
            {
                continue;
            }

            var entry = ParseFile(rootPath, filePath);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(currentPath))
        {
            ScanDirectory(rootPath, directoryPath, entries, cancellationToken);
        }
    }

    private static TranslatableEntry? ParseFile(string rootPath, string filePath)
    {
        try
        {
            var jsonText = File.ReadAllText(filePath);

            using var doc = JsonDocument.Parse(jsonText, JsonOptions);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Extract itemName or objectName
            var itemName = GetStringProperty(root, "itemName")
                        ?? GetStringProperty(root, "objectName")
                        ?? Path.GetFileNameWithoutExtension(filePath);

            // Extract translatable fields
            var sourceFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fieldName in TranslatableFields)
            {
                if (root.TryGetProperty(fieldName, out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    var value = prop.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        sourceFields[fieldName] = value;
                    }
                }
            }

            // Skip files with no translatable fields
            if (sourceFields.Count == 0)
            {
                return null;
            }

            var relativePath = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
            var fileType = Path.GetExtension(filePath).ToLowerInvariant();

            return new TranslatableEntry
            {
                RelativePath = relativePath,
                ItemName = itemName,
                FileType = fileType,
                SourceFields = sourceFields,
                TranslatedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
        }
        catch (Exception)
        {
            // Skip files that can't be parsed
            return null;
        }
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }

        return null;
    }
}
