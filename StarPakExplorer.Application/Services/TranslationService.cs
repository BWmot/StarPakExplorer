using System.Text.Json;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Services;

public sealed class TranslationService : ITranslationService
{
    private const int DefaultBatchSize = 30;

    private readonly ITranslationProjectStore projectStore;
    private readonly ITranslationEngine googleEngine;
    private readonly ITranslationEngine openAiEngine;
    private readonly ITranslationEngine googleFreeEngine;
    private readonly IGlobalGlossaryStore globalGlossaryStore;
    private readonly IAppLogger logger;

    public TranslationService(
        ITranslationProjectStore projectStore,
        ITranslationEngine googleEngine,
        ITranslationEngine openAiEngine,
        ITranslationEngine googleFreeEngine,
        IGlobalGlossaryStore globalGlossaryStore,
        IAppLogger logger)
    {
        this.projectStore = projectStore;
        this.googleEngine = googleEngine;
        this.openAiEngine = openAiEngine;
        this.googleFreeEngine = googleFreeEngine;
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

            await EnsureGlossaryAsync(existing, cancellationToken).ConfigureAwait(false);
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
        await EnsureGlossaryAsync(project, cancellationToken).ConfigureAwait(false);
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
        var glossary = await EnsureGlossaryAsync(project, cancellationToken).ConfigureAwait(false);
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

                translationCache[TranslationTextTools.BuildCacheKey(project.ProviderSettings.TargetLanguage, item.Entry.Original)] = translated;
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

        var glossary = await EnsureGlossaryAsync(project, cancellationToken).ConfigureAwait(false);
        var translationCache = await projectStore.LoadTranslationsCacheAsync(project.ProjectKey, cancellationToken).ConfigureAwait(false);
        var cacheKey = TranslationTextTools.BuildCacheKey(project.ProviderSettings.TargetLanguage, sourceText);
        if (translationCache.TryGetValue(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
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
            translationCache[cacheKey] = result;
            await projectStore.SaveTranslationsCacheAsync(project.ProjectKey, translationCache, cancellationToken).ConfigureAwait(false);

            // Persist the newly translated term into the project glossary so it is
            // reused within this project and synced to the global glossary below.
            // (Without this, new terms only land in the translation cache and never
            // reach the global glossary — the sync reads the project glossary.)
            if (!glossary.Lookup.TryGetValue(sourceText, out var current) ||
                !string.Equals(current, result, StringComparison.Ordinal))
            {
                glossary.Lookup[sourceText] = result;
                await projectStore.SaveGlossaryAsync(project.ProjectKey, glossary.Lookup, cancellationToken).ConfigureAwait(false);
            }
        }

        await SyncToGlobalGlossaryAsync(project, cancellationToken).ConfigureAwait(false);

        return result;
    }

    public async Task<int> ImportExistingTranslationsAsync(
        TranslationProgressDocument project,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(project.OutputDirectory) ||
            !Directory.Exists(project.OutputDirectory))
        {
            return 0;
        }

        var outputRoot = Path.GetFullPath(project.OutputDirectory);
        var outputFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(outputRoot, "*", SearchOption.AllDirectories))
        {
            outputFiles[Path.GetRelativePath(outputRoot, path).Replace('\\', '/')] = path;
        }

        var importedCount = 0;
        var matchedFileCount = 0;

        foreach (var file in project.Files.Where(item => item.IsSelected))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var effectiveMode = ResolveEffectiveMode(file);
            var outputRelPath = effectiveMode == TranslationGenerationMode.Patch
                ? $"{file.RelativePath}.patch"
                : file.RelativePath;

            if (!TryFindOutputFile(outputFiles, outputRelPath, out var outputFilePath))
            {
                continue;
            }

            // 已翻译的条目不覆盖，只回填尚未翻译的。
            var pathToValue = effectiveMode == TranslationGenerationMode.Patch
                ? ParsePatchFile(outputFilePath)
                : ParseOverwriteFile(outputFilePath);

            if (pathToValue is null || pathToValue.Count == 0)
            {
                continue;
            }

            var fileImported = 0;
            foreach (var entry in file.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(entry.Translated))
                {
                    continue;
                }

                if (!pathToValue.TryGetValue(entry.Path, out var translated) ||
                    string.IsNullOrWhiteSpace(translated) ||
                    string.Equals(translated, entry.Original, StringComparison.Ordinal))
                {
                    continue;
                }

                entry.Translated = translated;
                entry.Status = TranslationEntryStatus.Translated;
                entry.IsManuallyEdited = false;
                fileImported++;
            }

            if (fileImported > 0)
            {
                importedCount += fileImported;
                matchedFileCount++;
                progress?.Report($"已从 {file.RelativePath} 导入 {fileImported} 条翻译");
            }
        }

        if (importedCount > 0)
        {
            await SaveProjectAsync(project, cancellationToken).ConfigureAwait(false);
            progress?.Report($"导入完成：{matchedFileCount} 个文件，共 {importedCount} 条翻译。");
        }

        return importedCount;
    }

    private static bool TryFindOutputFile(
        IReadOnlyDictionary<string, string> outputFiles,
        string outputRelPath,
        out string filePath)
    {
        if (outputFiles.TryGetValue(outputRelPath, out filePath!))
        {
            return true;
        }

        // 兼容补丁目录外层多包了一层文件夹的情况：按后缀匹配。
        var suffix = "/" + outputRelPath;
        foreach (var (relative, path) in outputFiles)
        {
            if (relative.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                filePath = path;
                return true;
            }
        }

        filePath = "";
        return false;
    }

    /// <summary>
    /// 解析 .patch 补丁文件：数组形式的 [{"op":"replace","path":"/name","value":"..."} ...]。
    /// 返回 去前导斜杠的路径 → 译文 的映射。
    /// </summary>
    private static Dictionary<string, string>? ParsePatchFile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            using var document = JsonDocument.Parse(json);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var operation in document.RootElement.EnumerateArray())
            {
                if (operation.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var path = operation.TryGetProperty("path", out var pathProperty)
                    ? pathProperty.GetString()
                    : null;
                var value = operation.TryGetProperty("value", out var valueProperty)
                    ? valueProperty.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(path) || value is null)
                {
                    continue;
                }

                var key = path.StartsWith("/", StringComparison.Ordinal) ? path.Substring(1) : path;
                result[key] = value;
            }

            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 解析整文件覆盖模式输出的 JSON，遍历所有字符串值。
    /// 路径格式与条目 Path 一致（无前导斜杠，如 "name"、"items/0/label"）。
    /// </summary>
    private static Dictionary<string, string>? ParseOverwriteFile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            using var document = JsonDocument.Parse(json);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CollectStringValues(document.RootElement, "", result);
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void CollectStringValues(
        JsonElement element,
        string pointer,
        Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPointer = pointer.Length == 0
                        ? property.Name
                        : $"{pointer}/{property.Name}";
                    CollectStringValues(property.Value, childPointer, result);
                }

                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    CollectStringValues(item, $"{pointer}/{index}", result);
                    index++;
                }

                break;

            case JsonValueKind.String:
                if (!string.IsNullOrEmpty(pointer))
                {
                    result[pointer] = element.GetString() ?? "";
                }

                break;
        }
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
        var mergedGlossary = await EnsureGlossaryAsync(project, cancellationToken).ConfigureAwait(false);
        await projectStore.SaveGlossaryAsync(project.ProjectKey, mergedGlossary.Lookup, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TranslationGlossary> EnsureGlossaryAsync(TranslationProgressDocument project, CancellationToken cancellationToken)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Load project-local glossary; project entries override global fallback.
        var projectGlossary = await projectStore.LoadGlossaryAsync(project.ProjectKey, cancellationToken).ConfigureAwait(false);
        foreach (var (key, value) in projectGlossary)
        {
            lookup[key] = value;
        }

        // Merge global glossary as fallback, scoped to the target language.
        var targetLanguage = project.ProviderSettings.TargetLanguage;
        var globalEntries = await globalGlossaryStore.LoadByLanguageAsync(targetLanguage, cancellationToken).ConfigureAwait(false);

        // Track explicit term kinds from the DB so the built-in ambiguous set
        // can be overridden per entry (Default = force-substitute even if the
        // term is in the built-in ambiguous list).
        var explicitKindBySource = new Dictionary<string, GlossaryTermKind?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in globalEntries)
        {
            explicitKindBySource[entry.Source] = entry.TermKind;
            if (!lookup.ContainsKey(entry.Source))
            {
                lookup[entry.Source] = entry.Target;
            }
        }

        // If still empty, fall back to the built-in default glossary.
        var defaultGlossary = TranslationTextTools.BuildDefaultGlossary();
        if (lookup.Count == 0)
        {
            foreach (var (key, value) in defaultGlossary.Lookup)
            {
                lookup[key] = value;
            }
        }

        // Assemble the ambiguous set: explicit DB entries win; built-in defaults
        // fill the gaps unless explicitly marked Default.
        var ambiguousTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in globalEntries)
        {
            if (entry.TermKind == GlossaryTermKind.Ambiguous)
            {
                ambiguousTerms.Add(entry.Source);
            }
        }

        foreach (var term in defaultGlossary.AmbiguousTerms)
        {
            if (!lookup.ContainsKey(term))
            {
                continue;
            }

            if (explicitKindBySource.TryGetValue(term, out var kind) && kind == GlossaryTermKind.Default)
            {
                continue;
            }

            ambiguousTerms.Add(term);
        }

        // Save the merged lookup back to the project store.
        await projectStore.SaveGlossaryAsync(project.ProjectKey, lookup, cancellationToken).ConfigureAwait(false);

        return new TranslationGlossary
        {
            Lookup = lookup,
            AmbiguousTerms = ambiguousTerms
        };
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
            TranslationEngineType.GoogleFree => googleFreeEngine,
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
            var targetLanguage = project.ProviderSettings.TargetLanguage;
            var entries = glossary
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => new TranslationGlossaryEntry
                {
                    Source = pair.Key,
                    Target = pair.Value,
                    Language = targetLanguage,
                    EntrySource = GlossaryEntrySource.AutoFromCache,
                    ModifiedAt = now
                })
                .ToList();

            if (entries.Count > 0)
            {
                await globalGlossaryStore.UpsertManyAsync(entries, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"Failed to sync terms to global glossary: {ex.Message}", ex);
        }
    }
}
