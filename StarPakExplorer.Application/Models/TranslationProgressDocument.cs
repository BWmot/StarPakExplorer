namespace StarPakExplorer.Application.Models;

public sealed class TranslationProgressDocument
{
    public string ProjectKey { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string SourcePakPath { get; set; } = "";
    public string SourceCacheKey { get; set; } = "";
    public string SourceModName { get; set; } = "";
    public string? SourceModInternalName { get; set; }
    public string? SourceModVersion { get; set; }
    public string? SourceAuthor { get; set; }
    public string OutputDirectory { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public TranslationProviderSettings ProviderSettings { get; set; } = new();
    public List<TranslationFileState> Files { get; set; } = [];
}
