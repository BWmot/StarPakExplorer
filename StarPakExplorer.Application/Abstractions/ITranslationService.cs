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
