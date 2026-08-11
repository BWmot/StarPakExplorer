using System.Text.Encodings.Web;
using System.Text.Json;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Infrastructure.Translation;

public sealed class TranslationPatchWriter : ITranslationPatchWriter
{
    private static readonly JsonSerializerOptions PatchJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        // Starbound 的 .patch 要求键全部小写：op / path / value。
        // 之前缺此行导致输出 "Op"/"Path"/"Value"，游戏报 JsonException: No such key in Json::get("op")。
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public Task WriteTranslationModAsync(
        string outputPath,
        IReadOnlyList<TranslatableEntry> entries,
        TranslationModMetadata metadata,
        string originalModName,
        CancellationToken cancellationToken = default)
    {
        // Ensure output directory exists
        Directory.CreateDirectory(outputPath);

        // Write _metadata file
        WriteMetadata(outputPath, metadata, originalModName);

        // Group entries by relative path to handle multiple entries per file
        var entriesByPath = entries.GroupBy(e => e.RelativePath);

        foreach (var group in entriesByPath)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = group.Key;
            var fileEntries = group.ToList();

            // Build patch operations
            var patchOperations = new List<JsonPatchOperation>();

            foreach (var entry in fileEntries)
            {
                foreach (var sourceField in entry.SourceFields)
                {
                    var fieldName = sourceField.Key;
                    var originalValue = sourceField.Value;

                    // Check if we have a translation for this field
                    if (!entry.TranslatedFields.TryGetValue(fieldName, out var translatedValue))
                    {
                        continue;
                    }

                    // Skip empty or unchanged translations
                    if (string.IsNullOrWhiteSpace(translatedValue) ||
                        string.Equals(translatedValue, originalValue, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    patchOperations.Add(new JsonPatchOperation
                    {
                        Op = "replace",
                        Path = $"/{fieldName}",
                        Value = translatedValue
                    });
                }
            }

            // Only write patch file if there are actual changes
            if (patchOperations.Count > 0)
            {
                WritePatchFile(outputPath, relativePath, patchOperations);
            }
        }

        return Task.CompletedTask;
    }

    private static void WriteMetadata(
        string outputPath,
        TranslationModMetadata metadata,
        string originalModName)
    {
        var metadataObj = new Dictionary<string, object>
        {
            ["version"] = metadata.Version,
            ["author"] = metadata.Author,
            ["name"] = metadata.ModName,
            ["description"] = metadata.Description,
            ["friendlyName"] = metadata.FriendlyName,
            ["link"] = metadata.Link,
            ["priority"] = metadata.Priority,
            ["requires"] = new[] { originalModName }
        };

        var json = JsonSerializer.Serialize(metadataObj, MetadataJsonOptions);
        var metadataPath = Path.Combine(outputPath, "_metadata");
        File.WriteAllText(metadataPath, json);
    }

    private static void WritePatchFile(
        string outputPath,
        string relativePath,
        List<JsonPatchOperation> operations)
    {
        var patchFilePath = Path.Combine(outputPath, relativePath + ".patch");
        var patchDir = Path.GetDirectoryName(patchFilePath);

        if (!string.IsNullOrEmpty(patchDir))
        {
            Directory.CreateDirectory(patchDir);
        }

        var json = JsonSerializer.Serialize(operations, PatchJsonOptions);
        File.WriteAllText(patchFilePath, json);
    }

    private sealed class JsonPatchOperation
    {
        public string Op { get; init; } = "replace";
        public string Path { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
    }
}
