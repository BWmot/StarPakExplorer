namespace StarPakExplorer.Application.Models;

public sealed class PatchOverview
{
    public long TotalBytes { get; set; }
    public int PatchSetCount { get; set; }
    public IReadOnlyList<PatchSetSummary> PatchSets { get; set; } = [];
}
