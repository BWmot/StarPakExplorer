using System.Diagnostics;
using StarPakExplorer.Application.Abstractions;

namespace StarPakExplorer.Infrastructure.Unpacking;

public sealed class AssetUnpacker : IAssetUnpacker
{
    private readonly IAppLogger logger;

    public AssetUnpacker(IAppLogger logger)
    {
        this.logger = logger;
    }

    public async Task UnpackAsync(
        string unpackerPath,
        string pakPath,
        string outputDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = unpackerPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(pakPath);
        startInfo.ArgumentList.Add(outputDirectory);

        using var process = new Process { StartInfo = startInfo };
        logger.Info($"Starting asset_unpacker: {pakPath}");

        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动 asset_unpacker.exe");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;
        if (!string.IsNullOrWhiteSpace(output))
        {
            progress?.Report(output.Trim());
            logger.Info(output.Trim());
        }

        if (process.ExitCode != 0)
        {
            logger.Error($"asset_unpacker failed. ExitCode={process.ExitCode}. Error={error}");
            throw new InvalidOperationException($"asset_unpacker.exe 解包失败，退出码 {process.ExitCode}\n{error}");
        }
    }
}
