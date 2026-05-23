namespace StarPakExplorer.Application.Models;

public sealed class ResourceFileRecord
{
    public string RelativePath { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string Extension { get; set; } = "";
    public long SizeBytes { get; set; }
}
