using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Abstractions;

public interface ITranslationService
{
    Task<TranslationProgressDocument> LoadOrCreateProjectAsync(
        PakManifest manifest,
        string translationRootDirectory,
        CancellationToken cancellationToken);

    Task ScanAsync(
        TranslationProgressDocument project,
        PakManifest manifest,
        IProgress<string>? progress,
        CancellationToken cancellationToken);

    Task TranslatePendingAsync(
        TranslationProgressDocument project,
        IProgress<string>? progress,
        CancellationToken cancellationToken);

    Task<string> TranslateSingleAsync(
        TranslationProgressDocument project,
        string sourceText,
        CancellationToken cancellationToken);

    Task GenerateOutputAsync(
        TranslationProgressDocument project,
        PakManifest manifest,
        IProgress<string>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// 从输出目录中已存在的补丁（.patch）或整文件覆盖结果中导入已有翻译，
    /// 自动回填到项目里尚未翻译的条目。返回回填的条目数。
    /// </summary>
    Task<int> ImportExistingTranslationsAsync(
        TranslationProgressDocument project,
        IProgress<string>? progress,
        CancellationToken cancellationToken);

    Task<string> GenerateSingleEntryPatchAsync(
        TranslationProgressDocument project,
        TranslationFileState file,
        TranslationEntryState entry,
        CancellationToken cancellationToken);

    Task<string> GenerateReportAsync(
        TranslationProgressDocument project,
        CancellationToken cancellationToken);

    Task SaveProjectAsync(
        TranslationProgressDocument project,
        CancellationToken cancellationToken);
}
