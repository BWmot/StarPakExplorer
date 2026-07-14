namespace StarPakExplorer.Application.Models;

/// <summary>
/// 表示一个可翻译的条目（对应一个物品/对象文件中的可翻译字段集合）。
/// </summary>
public sealed class TranslatableEntry
{
    /// <summary>原始文件在 Mod 内的相对路径（如 "items/sexbound/crafting/sexbound_emptyjar.item"）</summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>物品的内部名称（itemName / objectName）</summary>
    public string ItemName { get; init; } = string.Empty;

    /// <summary>文件类型：.item / .activeitem / .object / .matitem / .codex</summary>
    public string FileType { get; init; } = string.Empty;

    /// <summary>需要翻译的字段及其原文。Key = JSON 字段名，Value = 当前英文原文</summary>
    public Dictionary<string, string> SourceFields { get; init; } = new();

    /// <summary>用户填写的翻译。Key = JSON 字段名，Value = 翻译后文本（空或等于原文则不生成 patch）</summary>
    public Dictionary<string, string> TranslatedFields { get; init; } = new();
}
