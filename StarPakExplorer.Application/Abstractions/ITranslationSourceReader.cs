using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Abstractions;

/// <summary>
/// 从解包后的 Mod 目录中读取所有可翻译条目。
/// </summary>
public interface ITranslationSourceReader
{
    /// <summary>
    /// 扫描目录，返回所有可翻译的条目列表。
    /// </summary>
    /// <param name="unpackedModPath">解包后的 Mod 根目录路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyList<TranslatableEntry>> ReadEntriesAsync(
        string unpackedModPath,
        CancellationToken cancellationToken = default);
}
