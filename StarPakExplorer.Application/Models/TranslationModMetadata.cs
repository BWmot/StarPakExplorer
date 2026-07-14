namespace StarPakExplorer.Application.Models;

public sealed class TranslationModMetadata
{
    public string Version { get; set; } = "2.1.1";
    public string Author { get; set; } = string.Empty;
    public string ModName { get; set; } = string.Empty;         // 如 "sexbound_ls_chinese"
    public string Description { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;   // 如 "[SxB] Language Support (中文)"
    public string Link { get; set; } = string.Empty;
    public int Priority { get; set; } = -68;
}
