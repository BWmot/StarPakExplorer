namespace StarPakExplorer.Application.Models;

public sealed class TranslationFileState
{
    public string RelativePath { get; set; } = "";
    public string SourceFullPath { get; set; } = "";
    public bool IsSelected { get; set; } = true;
    public TranslationGenerationMode GenerationMode { get; set; } = TranslationGenerationMode.Auto;
    public string SuggestedMode { get; set; } = "";
    public string SourceFingerprint { get; set; } = "";
    public long SourceSizeBytes { get; set; }
    public DateTime SourceLastWriteTimeUtc { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastScannedAt { get; set; }
    public List<TranslationEntryState> Entries { get; set; } = [];
}
