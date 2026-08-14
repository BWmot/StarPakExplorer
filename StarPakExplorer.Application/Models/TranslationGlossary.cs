namespace StarPakExplorer.Application.Models;

/// <summary>
/// 一次翻译任务使用的术语表快照。
/// - <see cref="Lookup"/>：源词 → 目标词映射（大小写不敏感）。
/// - <see cref="AmbiguousTerms"/>：单字多义词集合，仅当整段文本恰好等于该词时才套用术语表。
/// </summary>
public sealed class TranslationGlossary
{
    public Dictionary<string, string> Lookup { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> AmbiguousTerms { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
