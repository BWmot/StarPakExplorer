namespace StarPakExplorer.Application.Models;

public sealed class AppSettings
{
    public string AssetUnpackerPath { get; set; } = "";
    public string AssetPackerPath { get; set; } = "";
    public string PakParentDirectory { get; set; } = "";
    public string PatchRootDirectory { get; set; } = "";
    public string CacheRootDirectory { get; set; } = "";
}
