using System.Diagnostics;
using StarPakExplorer.Application.Abstractions;

namespace StarPakExplorer.Infrastructure.Unpacking;

public sealed class AssetPacker : IAssetPacker
{
    private readonly IAppLogger logger;

    public AssetPacker(IAppLogger logger)
    {
        this.logger = logger;
    }

    public async Task PackAsync(
        string packerPath,
        string inputDirectory,
        string outputPakPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPakPath) ?? ".");

        var startInfo = new ProcessStartInfo
        {
            FileName = packerPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = inputDirectory
        };

        startInfo.ArgumentList.Add(inputDirectory);
        startInfo.ArgumentList.Add(outputPakPath);

        using var process = new Process { StartInfo = startInfo };
        logger.Info($"Starting asset_packer: {inputDirectory} -> {outputPakPath}");

        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动 asset_packer.exe");
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
            logger.Error($"asset_packer failed. ExitCode={process.ExitCode}. Error={error}");
            throw new InvalidOperationException($"asset_packer.exe 封包失败，退出码 {process.ExitCode}\n{error}");
        }
    }
}
