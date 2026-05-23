namespace StarPakExplorer.Application.Models;

public sealed class PakManifest
{
    public string PakPath { get; set; } = "";
    public string CacheKey { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public long PakSize { get; set; }
    public DateTime PakLastWriteTimeUtc { get; set; }
    public string? WorkshopId { get; set; }
    public string? ModName { get; set; }
    public string? Author { get; set; }
    public List<ResourceFileRecord> Files { get; set; } = [];
}
