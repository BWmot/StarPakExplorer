using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Abstractions;

/// <summary>
/// 根据翻译数据生成 .patch 文件和 _metadata。
/// </summary>
public interface ITranslationPatchWriter
{
    /// <summary>
    /// 将翻译条目写入输出目录，生成 .patch 文件和 _metadata。
    /// </summary>
    /// <param name="outputPath">输出目录（翻译 Mod 根目录）</param>
    /// <param name="entries">翻译完成的条目</param>
    /// <param name="metadata">翻译 Mod 的元数据</param>
    /// <param name="originalModName">被翻译的主 Mod 的 name（用于 requires）</param>
    Task WriteTranslationModAsync(
        string outputPath,
        IReadOnlyList<TranslatableEntry> entries,
        TranslationModMetadata metadata,
        string originalModName,
        CancellationToken cancellationToken = default);
}
