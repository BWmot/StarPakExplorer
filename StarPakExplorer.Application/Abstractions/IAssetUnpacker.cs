namespace StarPakExplorer.Application.Abstractions;

public interface IAssetUnpacker
{
    Task UnpackAsync(
        string unpackerPath,
        string pakPath,
        string outputDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}
