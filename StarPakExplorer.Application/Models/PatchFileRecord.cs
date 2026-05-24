namespace StarPakExplorer.Application.Models;

public sealed class PatchFileRecord
{
    public string RelativePath { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
}
