namespace StarPakExplorer.Application.Models;

public sealed class PakLoadResult
{
    public required PakManifest Manifest { get; init; }
    public bool LoadedFromCache { get; init; }
    public string StatusMessage { get; init; } = "";
}
