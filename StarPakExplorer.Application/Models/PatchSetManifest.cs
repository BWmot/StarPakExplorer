namespace StarPakExplorer.Application.Models;

public sealed class PatchSetManifest
{
    public string PatchKey { get; set; } = "";
    public string? WorkshopId { get; set; }
    public string? ModName { get; set; }
    public string? Author { get; set; }
    public string? SourcePakPath { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
