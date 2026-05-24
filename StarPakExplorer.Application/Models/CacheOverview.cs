namespace StarPakExplorer.Application.Models;

public sealed class CacheOverview
{
    public long TotalBytes { get; set; }
    public int EntryCount { get; set; }
    public IReadOnlyList<CacheEntrySummary> RecentEntries { get; set; } = [];
}
