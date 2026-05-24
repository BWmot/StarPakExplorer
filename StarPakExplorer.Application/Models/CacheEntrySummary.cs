namespace StarPakExplorer.Application.Models;

public sealed class CacheEntrySummary
{
    public string CacheKey { get; set; } = "";
    public string PakPath { get; set; } = "";
    public string? ModName { get; set; }
    public string? WorkshopId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CacheBytes { get; set; }
    public long PakSize { get; set; }
    public string DisplayName => !string.IsNullOrWhiteSpace(ModName)
        ? ModName!
        : Path.GetFileNameWithoutExtension(PakPath);
}
