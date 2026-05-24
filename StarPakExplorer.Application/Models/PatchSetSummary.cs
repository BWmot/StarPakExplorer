namespace StarPakExplorer.Application.Models;

public sealed class PatchSetSummary
{
    public string PatchKey { get; set; } = "";
    public string? WorkshopId { get; set; }
    public string? ModName { get; set; }
    public string? Author { get; set; }
    public string? SourcePakPath { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }

    public string DisplayName => !string.IsNullOrWhiteSpace(ModName)
        ? ModName!
        : (!string.IsNullOrWhiteSpace(WorkshopId) ? WorkshopId! : PatchKey);
}
