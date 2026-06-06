namespace StarPakExplorer.Application.Abstractions;

public interface IAssetPacker
{
    Task PackAsync(
        string packerPath,
        string inputDirectory,
        string outputPakPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}
