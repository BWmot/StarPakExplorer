namespace StarPakExplorer.Application.Models;

public sealed class TranslationFileAnalysis
{
    public string RelativePath { get; set; } = "";
    public string SourceFingerprint { get; set; } = "";
    public TranslationGenerationMode SuggestedMode { get; set; } = TranslationGenerationMode.Auto;
    public List<TranslationSourceEntry> Entries { get; set; } = [];
}
