using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Services;

public sealed class TranslationService : ITranslationService
{
    private const int DefaultBatchSize = 30;

    private readonly ITranslationProjectStore projectStore;
    private readonly ITranslationEngine googleEngine;
    private readonly ITranslationEngine openAiEngine;
    private readonly IGlobalGlossaryStore globalGlossaryStore;
    private readonly IAppLogger logger;

    public TranslationService(
        ITranslationProjectStore projectStore,
        ITranslationEngine googleEngine,
        ITranslationEngine openAiEngine,
        IGlobalGlossaryStore globalGlossaryStore,
        IAppLogger logger)
    {
        this.projectStore = projectStore;
        this.googleEngine = googleEngine;
        this.openAiEngine = openAiEngine;
        this.globalGlossaryStore = globalGlossaryStore;
        this.logger = logger;
    }

    public async Task<TranslationProgressDocument> LoadOrCreateProjectAsync(
        PakManifest manifest,
        string translationRootDirectory,
        CancellationToken cancellationToken)
    {
        var projectKey = projectStore.GetProjectKey(manifest);
        var existing = await projectStore.LoadProgressAsync(projectKey, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            var resolvedRoot = string.IsNullOrWhiteSpace(translationRootDirectory)
                ? projectStore.GetTranslationRoot()
                : translationRootDirectory;

            existing.ProjectKey = projectKey;
            existing.ProjectName = GetProjectName(manifest);
            existing.SourcePakPath = manifest.PakPath;
            existing.SourceCacheKey = manifest.CacheKey;
            existing.SourceModName = manifest.ModInternalName ?? manifest.ModName ?? GetProjectName(manifest);
            existing.SourceModInternalName = manifest.ModInternalName;
            existing.SourceModVersion = manifest.ModVersion;
            existing.SourceAuthor = manifest.Author;
            existing.OutputDirectory = EnsureOutputDirectory(existing.OutputDirectory, resolvedRoot, projectKey);
            existing.ProviderSettings ??= new TranslationProviderSettings();

            if (existing.ProviderSettings.OpenAi is null)
            {
                existing.ProviderSettings.OpenAi = new OpenAiTranslationSettings();
            }

            if (existing.ProviderSettings.Google is null)
            {
                existing.ProviderSettings.Google = new GoogleTranslationSettings();
            }

            await EnsureGlossaryAsync(projectKey, cancellationToken).ConfigureAwait(false);
            return existing;
        }

        var project = new TranslationProgressDocument
        {
            ProjectKey = projectKey,
            ProjectName = GetProjectName(manifest),
            SourcePakPath = manifest.PakPath,
            SourceCacheKey = manifest.CacheKey,
            SourceModName = manifest.ModInternalName ?? manifest.ModName ?? GetProjectName(manifest),
            SourceModInternalName = manifest.ModInternalName,
            SourceModVersion = manifest.ModVersion,
            SourceAuthor = manifest.Author,
            OutputDirectory = EnsureOutputDirectory("", string.IsNullOrWhiteSpace(translationRootDirectory) ? projectStore.GetTranslationRoot() : translationRootDirectory, projectKey),
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
            ProviderSettings = new TranslationProviderSettings()
        };

        await projectStore.SaveProgressAsync(project, cancellationToken).ConfigureAwait(false);
        await EnsureGlossaryAsync(projectKey, cancellationToken).ConfigureAwait(false);
        return project;
    }

    public async Task ScanAsync(
        TranslationProgressDocument project,
        PakManifest manifest,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var selectedFiles = manifest.Files
            .Where(file => TranslationTextTools.IsTranslatableCandidate(file.RelativePath))
            .ToList();

        progress?.Report($"正在扫描 {selectedFiles.Count} 个可翻译文件...");

        var existingByPath = project.Files
            .ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);

        var projectFiles = new List<TranslationFileState>();
        for (var index = 0; index < selectedFiles.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = selectedFiles[index];
            progress?.Report($"扫描 {index + 1}/{selectedFiles.Count}: {file.RelativePath}");

            try
            {
                var analysis = TranslationTextTools.AnalyzeFile(file.FullPath, file.RelativePath);
                var nextState = MergeFileState(project, existingByPath, analysis, file);
                projectFiles.Add(nextState);
            }
            catch (Exception exception)
            {
                logger.Warn($"Scan failed: {file.RelativePath}", exception);
                projectFiles.Add(new TranslationFileState
                {
                    RelativePath = file.RelativePath,
                    SourceFullPath = file.FullPath,
                    SourceSizeBytes = file.SizeBytes,
                    SourceLastWriteTimeUtc = File.GetLastWriteTimeUtc(file.FullPath),
                    SuggestedMode = TranslationTextTools.DetermineSuggestedMode(file.RelativePath).ToString(),
                    GenerationMode = existingByPath.TryGetValue(file.RelativePath, out var existingState)
                        ? existingState.GenerationMode
                        : TranslationGenerationMode.Auto,
                    SourceFingerprint = "",
                    LastError = exception.Message,
                    LastScannedAt = DateTimeOffset.Now
                });
            }
        }

        project.Files = projectFiles
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        project.UpdatedAt = DateTimeOffset.Now;

        await SaveProjectAsync(project, cancellationToken).ConfigureAwait(false);
    }

    public async Task TranslatePendingAsync(
        TranslationProgressDocument project,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var glossary = await EnsureGlossaryAsync(project.ProjectKey, cancellationToken).ConfigureAwait(false);
        var translationCache = await projectStore.LoadTranslationsCacheAsync(project.ProjectKey, cancellationToken).ConfigureAwait(false);
        var fileCache = await projectStore.LoadFileTranslationsAsync(project.ProjectKey, cancellationToken).ConfigureAwait(false);

        var pendingEntries = project.Files
            .Where(file => file.IsSelected)
            .SelectMany(file => file.Entries.Select(entry => new { File = file, Entry = entry }))
            .Where(item => string.IsNullOrWhiteSpace(item.Entry.Translated) && item.Entry.Status != TranslationEntryStatus.Skipped)
            .ToList();

        if (pendingEntries.Count == 0)
        {
            progress?.Report("没有待翻译文本。");
            return;
        }

        progress?.Report($"发现 {pendingEntries.Count} 条待翻译文本，开始批量翻译...");

        var engine = ResolveEngine(project.ProviderSettings.PreferredEngine);

        var uniquePending = pendingEntries
            .Where(item => !string.IsNullOrWhiteSpace(item.Entry.Original))
            .GroupBy(item => item.Entry.Original, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        for (var offset = 0; offset < uniquePending.Count; offset += DefaultBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = uniquePending.Skip(offset).Take(DefaultBatchSize).ToList();
            if (batch.Count == 0)
            {
                continue;
            }

            var sourceTexts = batch.Select(item => item.Entry.Original).ToList();
            progress?.Report($"翻译 {offset + 1}-{offset + batch.Count}/{uniquePending.Count}...");

            IReadOnlyList<string>? translations = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    translations = await engine.TranslateBatchAsync(sourceTexts, project.ProviderSettings, glossary, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (Exception exception) when (attempt < 3)
                {
                    logger.Warn($"Translation batch failed, retry {attempt}", exception);
                    var delay = TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }

            if (translations is null)
            {
                foreach (var item in batch)
                {
                    item.Entry.Status = TranslationEntryStatus.Failed;
                }

                continue;
            }

            for (var index = 0; index < batch.Count; index++)
            {
                var translated = translations[index];
                var item = batch[index];
                item.Entry.Translated = translated;
                item.Entry.Status = TranslationEntryStatus.Translated;
                item.Entry.IsManuallyEdited = false;

                translationCache[item.Entry.Original] = translated;
                if (!fileCache.TryGetValue(item.File.RelativePath, out var perFile))
                {
                    perFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    fileCache[item.File.RelativePath] = perFile;
                }

                perFile[item.Entry.Path] = translated;
            }

            project.UpdatedAt = DateTimeOffset.Now;
            await projectStore.SaveProgressAsync(project, cancellationToken).ConfigureAwait(false);
            await projectStore.SaveTranslationsCacheAsync(project.ProjectKey, translationCache, cancellationToken).ConfigureAwait(false);
            await projectStore.SaveFileTranslationsAsync(
                project.ProjectKey,
                fileCache.ToDictionary(pair => pair.Key, pair => (IDictionary<string, string>)pair.Value, StringComparer.OrdinalIgnoreCase),
                cancellationToken).ConfigureAwait(false);

            if (offset + batch.Count < uniquePending.Count)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
        }

        project.UpdatedAt = DateTimeOffset.Now;
        await SaveProjectAsync(project, cancellationToken).ConfigureAwait(false);
        await SyncToGlobalGlossaryAsync(project, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> TranslateSingleAsync(
        TranslationProgressDocument project,
        string sourceText,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return "";
        }

        var glossary = await EnsureGlossaryAsync(project.ProjectKey, cancellationToken).ConfigureAwait(false);
        var translationCache = await projectStore.LoadTranslationsCacheAsync(project.ProjectKey, cancellationToken).ConfigureAwait(false);
        if (translationCache.TryGetValue(sourceText, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var engine = ResolveEngine(project.ProviderSettings.PreferredEngine);
        var translations = await engine.TranslateBatchAsync(
            [sourceText],
            project.ProviderSettings,
            glossary,
            cancellationToken).ConfigureAwait(false);

        var result = translations.FirstOrDefault() ?? "";
        if (!string.IsNullOrWhiteSpace(result))
        {
            translationCache[sourceText] = result;
            await projectStore.SaveTranslationsCacheAsync(project.ProjectKey, translationCache, cancellationToken).ConfigureAwait(false);
        }

        await SyncToGlobalGlossaryAsync(project, cancellationToken).ConfigureAwait(false);

        return result;
    }

    public async Task GenerateOutputAsync(
        TranslationProgressDocument project,
        PakManifest manifest,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(project.OutputDirectory))
        {
            project.OutputDirectory = EnsureOutputDirectory("", projectStore.GetTranslationRoot(), project.ProjectKey);
        }

        Directory.CreateDirectory(project.OutputDirectory);

        var fileSummaries = new List<(TranslationFileState File, int TranslatedCount, int PendingCount)>();
        var outputFileCount = 0;

        progress?.Report("正在生成汉化补丁文件...");

        foreach (var file in project.Files.Where(item => item.IsSelected))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(file.SourceFullPath) || !File.Exists(file.SourceFullPath))
            {
                file.LastError = $"找不到源文件: {file.SourceFullPath}";
                continue;
            }

            var translatedEntries = file.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Translated))
                .ToList();

            var translatedCount = translatedEntries.Count;
            var pendingCount = file.Entries.Count - translatedCount;
            fileSummaries.Add((file, translatedCount, pendingCount));

            var effectiveMode = ResolveEffectiveMode(file);
            progress?.Report($"生成 {file.RelativePath} ({effectiveMode})");

            var outputPath = GetOutputPath(project.OutputDirectory, file.RelativePath, effectiveMode);
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            if (effectiveMode == TranslationGenerationMode.Patch)
            {
                var patchText = TranslationTextTools.BuildPatchFile(translatedEntries);
                await File.WriteAllTextAsync(outputPath, patchText, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var sourceBytes = await File.ReadAllBytesAsync(file.SourceFullPath, cancellationToken).ConfigureAwait(false);
                var translatedByPath = file.Entries.ToDictionary(
                    entry => entry.Path,
                    entry => entry.Translated ?? entry.Original,
                    StringComparer.OrdinalIgnoreCase);
                var outputBytes = TranslationTextTools.ApplyOverwriteTranslation(sourceBytes, translatedByPath);
                await File.WriteAllBytesAsync(outputPath, outputBytes, cancellationToken).ConfigureAwait(false);
            }

            outputFileCount++;
        }

        var metadataPath = Path.Combine(project.OutputDirectory, "_metadata");
        await File.WriteAllTextAsync(metadataPath, TranslationTextTools.BuildMetadataJson(project), cancellationToken).ConfigureAwait(false);

        var reportPath = Path.Combine(project.OutputDirectory, "report.html");
        var reportHtml = TranslationTextTools.BuildReportHtml(project, fileSummaries);
        await File.WriteAllTextAsync(reportPath, reportHtml, cancellationToken).ConfigureAwait(false);

        progress?.Report($"已生成 {outputFileCount} 个文件，报告已写入 report.html");

        project.UpdatedAt = DateTimeOffset.Now;
        await SaveProjectAsync(project, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GenerateSingleEntryPatchAsync(
        TranslationProgressDocument project,
        TranslationFileState file,
        TranslationEntryState entry,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(project.OutputDirectory))
        {
            project.OutputDirectory = EnsureOutputDirectory("", projectStore.GetTranslationRoot(), project.ProjectKey);
        }

        Directory.CreateDirectory(project.OutputDirectory);

        var relativePath = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);
        var outputPath = Path.Combine(project.OutputDirectory, $"{relativePath}.patch");
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var patchText = TranslationTextTools.BuildPatchFile([entry]);
        await File.WriteAllTextAsync(outputPath, patchText, cancellationToken).ConfigureAwait(false);
        await SaveProjectAsync(project, cancellationToken).ConfigureAwait(false);
        return outputPath;
    }

    public async Task<string> GenerateReportAsync(
        TranslationProgressDocument project,
        CancellationToken cancellationToken)
    {
        var fileSummaries = await BuildFileSummariesAsync(project, cancellationToken).ConfigureAwait(false);
        return TranslationTextTools.BuildReportHtml(project, fileSummaries);
    }

    public async Task SaveProjectAsync(
        TranslationProgressDocument project,
        CancellationToken cancellationToken)
    {
        project.UpdatedAt = DateTimeOffset.Now;
        await projectStore.SaveProgressAsync(project, cancellationToken).ConfigureAwait(false);

        var translationCache = TranslationTextTools.BuildTranslationCache(project);
        var fileCache = TranslationTextTools.BuildFileTranslationCache(project);
        await projectStore.SaveTranslationsCacheAsync(project.ProjectKey, translationCache, cancellationToken).ConfigureAwait(false);
        await projectStore.SaveFileTranslationsAsync(
            project.ProjectKey,
            fileCache.ToDictionary(pair => pair.Key, pair => (IDictionary<string, string>)pair.Value, StringComparer.OrdinalIgnoreCase),
            cancellationToken).ConfigureAwait(false);
        await projectStore.SaveGlossaryAsync(project.ProjectKey, await EnsureGlossaryAsync(project.ProjectKey, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, string>> EnsureGlossaryAsync(string projectKey, CancellationToken cancellationToken)
    {
        // Load project-local glossary
        var glossary = await projectStore.LoadGlossaryAsync(projectKey, cancellationToken).ConfigureAwait(false);

        // Merge global glossary as fallback (project glossary overrides global)
        var globalLookup = await globalGlossaryStore.BuildLookupAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (key, value) in globalLookup)
        {
            if (!glossary.ContainsKey(key))
            {
                glossary[key] = value;
            }
        }

        // If still empty, use the default (now loads from reference term banks)
        if (glossary.Count == 0)
        {
            glossary = TranslationTextTools.BuildDefaultGlossary();
        }

        // Save merged glossary back to project
        await projectStore.SaveGlossaryAsync(projectKey, glossary, cancellationToken).ConfigureAwait(false);

        return glossary;
    }

    private static TranslationFileState MergeFileState(
        TranslationProgressDocument project,
        IReadOnlyDictionary<string, TranslationFileState> existingByPath,
        TranslationFileAnalysis analysis,
        ResourceFileRecord file)
    {
        var existingState = existingByPath.TryGetValue(file.RelativePath, out var existing)
            ? existing
            : null;

        var next = new TranslationFileState
        {
            RelativePath = file.RelativePath,
            SourceFullPath = file.FullPath,
            IsSelected = existingState?.IsSelected ?? true,
            GenerationMode = existingState?.GenerationMode ?? TranslationGenerationMode.Auto,
            SuggestedMode = analysis.SuggestedMode.ToString(),
            SourceFingerprint = analysis.SourceFingerprint,
            SourceSizeBytes = file.SizeBytes,
            SourceLastWriteTimeUtc = File.GetLastWriteTimeUtc(file.FullPath),
            LastScannedAt = DateTimeOffset.Now,
            LastError = null
        };

        var previousEntries = existingState is not null
            ? existingState.Entries.ToDictionary(entry => $"{entry.Path}|{entry.Original}", StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, TranslationEntryState>(StringComparer.OrdinalIgnoreCase);

        var translationsByOriginal = project.Files
            .SelectMany(item => item.Entries)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Translated))
            .GroupBy(entry => entry.Original, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Translated!, StringComparer.Ordinal);

        foreach (var entry in analysis.Entries)
        {
            var nextEntry = new TranslationEntryState
            {
                Path = entry.Path,
                Original = entry.Original,
            OriginalHash = ComputeHash(entry.Original)
            };

            if (previousEntries.TryGetValue($"{entry.Path}|{entry.Original}", out var preserved))
            {
                nextEntry.Translated = preserved.Translated;
                nextEntry.Status = preserved.Status;
                nextEntry.IsManuallyEdited = preserved.IsManuallyEdited;
            }
            else if (translationsByOriginal.TryGetValue(entry.Original, out var translated))
            {
                nextEntry.Translated = translated;
                nextEntry.Status = TranslationEntryStatus.Translated;
            }

            if (string.IsNullOrWhiteSpace(nextEntry.Translated) && string.IsNullOrWhiteSpace(entry.Original))
            {
                nextEntry.Status = TranslationEntryStatus.Pending;
            }

            next.Entries.Add(nextEntry);
        }

        return next;
    }

    private static TranslationGenerationMode ResolveEffectiveMode(TranslationFileState file)
    {
        if (file.GenerationMode != TranslationGenerationMode.Auto)
        {
            return file.GenerationMode;
        }

        return Enum.TryParse<TranslationGenerationMode>(file.SuggestedMode, true, out var mode)
            ? mode
            : TranslationGenerationMode.FileOverwrite;
    }

    private static string GetOutputPath(string outputDirectory, string relativePath, TranslationGenerationMode mode)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        if (mode == TranslationGenerationMode.Patch)
        {
            return Path.Combine(outputDirectory, $"{normalized}.patch");
        }

        return Path.Combine(outputDirectory, normalized);
    }

    private static async Task<List<(TranslationFileState File, int TranslatedCount, int PendingCount)>> BuildFileSummariesAsync(
        TranslationProgressDocument project,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return project.Files
            .Select(file =>
            {
                var translatedCount = file.Entries.Count(entry => !string.IsNullOrWhiteSpace(entry.Translated));
                return (file, translatedCount, file.Entries.Count - translatedCount);
            })
            .ToList();
    }

    private ITranslationEngine ResolveEngine(TranslationEngineType engineType)
    {
        return engineType switch
        {
            TranslationEngineType.Google => googleEngine,
            TranslationEngineType.OpenAI => openAiEngine,
            _ => openAiEngine
        };
    }

    private static string EnsureOutputDirectory(string existingPath, string translationRootDirectory, string projectKey)
    {
        if (!string.IsNullOrWhiteSpace(existingPath))
        {
            return existingPath;
        }

        if (string.IsNullOrWhiteSpace(translationRootDirectory))
        {
            return "";
        }

        return Path.Combine(translationRootDirectory, projectKey);
    }

    private static string GetProjectName(PakManifest manifest)
    {
        return $"CN_{SanitizeName(manifest.ModName ?? Path.GetFileNameWithoutExtension(manifest.PakPath))}_zhCN";
    }

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return string.IsNullOrWhiteSpace(builder.ToString()) ? "patch" : builder.ToString().Trim('.');
    }

    private static string ComputeHash(string value)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private async Task SyncToGlobalGlossaryAsync(TranslationProgressDocument project, CancellationToken cancellationToken)
    {
        try
        {
            var glossary = await projectStore.LoadGlossaryAsync(project.ProjectKey, cancellationToken).ConfigureAwait(false);
            if (glossary.Count == 0) return;

            var now = DateTimeOffset.Now;
            foreach (var (source, target) in glossary)
            {
                if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)) continue;
                await globalGlossaryStore.UpsertAsync(new TranslationGlossaryEntry
                {
                    Source = source,
                    Target = target,
                    EntrySource = GlossaryEntrySource.AutoFromCache,
                    ModifiedAt = now
                }, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"Failed to sync terms to global glossary: {ex.Message}", ex);
        }
    }
}
