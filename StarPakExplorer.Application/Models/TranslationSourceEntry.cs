namespace StarPakExplorer.Application.Models;

public sealed class TranslationSourceEntry
{
    public string Path { get; set; } = "";
    public string Original { get; set; } = "";
    public long TokenStartIndex { get; set; }
    public long TokenEndIndex { get; set; }
}
