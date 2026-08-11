using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;
using StarPakExplorer.Application.Services;

namespace StarPakExplorer.Infrastructure.Translation;

/// <summary>
/// 从解包后的 Mod 目录中读取所有可翻译条目。
/// 扩展名白名单与字段提取逻辑统一委托给 <see cref="TranslationTextTools"/>，
/// 与翻译管理器的判定保持一致（含 dialog/.npctype/.questtemplate/interface/.species 等类别）。
/// </summary>
public sealed class TranslationSourceReader : ITranslationSourceReader
{
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

            if (!TranslationTextTools.IsTranslatableCandidate(filePath))
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
            var fileType = Path.GetExtension(filePath);

            var itemName = TranslationTextTools.GetItemName(jsonText, Path.GetFileNameWithoutExtension(filePath));

            var sourceFields = TranslationTextTools.ExtractTranslatableFields(jsonText, fileType);

            // 跳过没有任何可翻译字段的文件。
            if (sourceFields.Count == 0)
            {
                return null;
            }

            var relativePath = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');

            return new TranslatableEntry
            {
                RelativePath = relativePath,
                ItemName = itemName,
                FileType = fileType.ToLowerInvariant(),
                SourceFields = sourceFields,
                TranslatedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
        }
        catch (Exception)
        {
            // 跳过无法解析的文件。
            return null;
        }
    }
}
