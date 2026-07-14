namespace StarPakExplorer.Application.Models;

public sealed class TranslationEntryState
{
    public string Path { get; set; } = "";
    public string Original { get; set; } = "";
    public string OriginalHash { get; set; } = "";
    public string? Translated { get; set; }
    public TranslationEntryStatus Status { get; set; } = TranslationEntryStatus.Pending;
    public bool IsManuallyEdited { get; set; }
}
