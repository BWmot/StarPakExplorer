using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;
using StarPakExplorer.Infrastructure.Translation;
using StarPakExplorer.UI.Commands;

namespace StarPakExplorer.UI.ViewModels;

public sealed class TranslationManagerViewModel : ViewModelBase
{
    private readonly ITranslationService translationService;
    private readonly IAppLogger logger;
    private readonly AppSettings appSettings;
    private readonly TranslationEngineCache engineCache;
    private readonly PakManifest manifest;
    private readonly CancellationTokenSource cancellationTokenSource = new();

    private TranslationProgressDocument? project;
    private string outputDirectory = "";
    private string projectTitle = "";
    private string projectSummary = "尚未加载项目。";
    private string statusMessage = "就绪。";
    private bool isBusy;
    private string fileFilterText = "";
    private TranslationFileViewModel? selectedFile;
    private TranslationEntryViewModel? selectedEntry;
    private string selectedPath = "";
    private string selectedOriginal = "";
    private string entryTranslationDraft = "";
    private string selectedStatus = "";
    private System.ComponentModel.ICollectionView? filesView;
    private System.ComponentModel.ICollectionView? entriesView;

    // ── Engine configuration ──
    private string targetLanguage = "zh-CN";
    private string openAiApiKey = "";
    private string openAiModel = "gpt-4.1-mini";
    private string openAiBaseUrl = "https://api.openai.com/v1";
    private string googleProjectId = "";
    private string googleLocation = "global";
    private string googleServiceAccountJsonPath = "";

    public TranslationManagerViewModel(
        ITranslationService translationService,
        IAppLogger logger,
        AppSettings appSettings,
        TranslationEngineCache engineCache,
        PakManifest manifest)
    {
        this.translationService = translationService;
        this.logger = logger;
        this.appSettings = appSettings;
        this.engineCache = engineCache;
        this.manifest = manifest;

        Files = new ObservableCollection<TranslationFileViewModel>();
        Entries = new ObservableCollection<TranslationEntryViewModel>();

        filesView = System.Windows.Data.CollectionViewSource.GetDefaultView(Files);
        entriesView = System.Windows.Data.CollectionViewSource.GetDefaultView(Entries);

        filesView.Filter = FilterFile;
        entriesView.Filter = FilterEntry;

        ProjectTitle = string.IsNullOrWhiteSpace(manifest.ModName)
            ? Path.GetFileNameWithoutExtension(manifest.PakPath)
            : manifest.ModName;

        OutputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "StarPakTranslations",
            SanitizeFileName(ProjectTitle));

        BrowseOutputDirectoryCommand = new RelayCommand(BrowseOutputDirectory, () => !IsBusy);
        OpenOutputDirectoryCommand = new RelayCommand(OpenOutputDirectory, () => !IsBusy && Directory.Exists(OutputDirectory));
        SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync, () => !IsBusy && project is not null);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        TranslateCommand = new AsyncRelayCommand(TranslateAsync, () => !IsBusy && project is not null && project.Files.Count > 0);
        GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => !IsBusy && project is not null && project.Files.Count > 0);
        ImportExistingCommand = new AsyncRelayCommand(ImportExistingAsync, () => !IsBusy && project is not null && project.Files.Count > 0);
        SelectFilteredFilesCommand = new RelayCommand(SelectFilteredFiles, () => !IsBusy);
        ClearFilteredFilesCommand = new RelayCommand(ClearFilteredFiles, () => !IsBusy);
        TranslateCurrentEntryCommand = new AsyncRelayCommand(TranslateCurrentEntryAsync, () => !IsBusy && SelectedEntry is not null);
        SaveCurrentEntryCommand = new AsyncRelayCommand(SaveCurrentEntryAsync, () => !IsBusy && SelectedEntry is not null && !string.IsNullOrWhiteSpace(EntryTranslationDraft));
        SkipCurrentEntryCommand = new RelayCommand(SkipCurrentEntry, () => !IsBusy && SelectedEntry is not null);
        ConfirmCurrentEntryCommand = new RelayCommand(ConfirmCurrentEntry, () => !IsBusy && SelectedEntry is not null && SelectedEntry.Status == TranslationEntryStatus.Translated);
        BrowseGoogleServiceAccountCommand = new RelayCommand(BrowseGoogleServiceAccount, () => !IsBusy);

        // Load or create project asynchronously
        _ = InitializeProjectAsync();
    }

    // ═══════════════════════════════════════════════
    //  Collections
    // ═══════════════════════════════════════════════

    public ObservableCollection<TranslationFileViewModel> Files { get; }

    public ObservableCollection<TranslationEntryViewModel> Entries { get; }

    // ═══════════════════════════════════════════════
    //  Commands
    // ═══════════════════════════════════════════════

    public RelayCommand BrowseOutputDirectoryCommand { get; }
    public RelayCommand OpenOutputDirectoryCommand { get; }
    public AsyncRelayCommand SaveProjectCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand TranslateCommand { get; }
    public AsyncRelayCommand GenerateCommand { get; }
    public AsyncRelayCommand ImportExistingCommand { get; }
    public RelayCommand SelectFilteredFilesCommand { get; }
    public RelayCommand ClearFilteredFilesCommand { get; }
    public AsyncRelayCommand TranslateCurrentEntryCommand { get; }
    public AsyncRelayCommand SaveCurrentEntryCommand { get; }
    public RelayCommand SkipCurrentEntryCommand { get; }
    public RelayCommand ConfirmCurrentEntryCommand { get; }
    public RelayCommand BrowseGoogleServiceAccountCommand { get; }

    // ═══════════════════════════════════════════════
    //  Properties
    // ═══════════════════════════════════════════════

    public string ManifestPath => manifest.PakPath;

    public string OutputDirectory
    {
        get => outputDirectory;
        set
        {
            if (SetProperty(ref outputDirectory, value))
            {
                if (project is not null)
                {
                    project.OutputDirectory = value;
                }

                OpenOutputDirectoryCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ProjectTitle
    {
        get => projectTitle;
        set => SetProperty(ref projectTitle, value);
    }

    public string ProjectSummary
    {
        get => projectSummary;
        set => SetProperty(ref projectSummary, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        set => SetProperty(ref statusMessage, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        set
        {
            if (SetProperty(ref isBusy, value))
            {
                RefreshCommandStates();
            }
        }
    }

    public string FileFilterText
    {
        get => fileFilterText;
        set
        {
            if (SetProperty(ref fileFilterText, value))
            {
                filesView?.Refresh();
            }
        }
    }

    public System.ComponentModel.ICollectionView FilesView => filesView!;

    public TranslationFileViewModel? SelectedFile
    {
        get => selectedFile;
        set
        {
            if (SetProperty(ref selectedFile, value))
            {
                RefreshEntriesForSelectedFile();
            }
        }
    }

    public TranslationEntryViewModel? SelectedEntry
    {
        get => selectedEntry;
        set
        {
            if (SetProperty(ref selectedEntry, value))
            {
                if (value is not null)
                {
                    SelectedPath = value.Path;
                    SelectedOriginal = value.Original;
                    EntryTranslationDraft = value.Translated ?? "";
                    SelectedStatus = value.StatusLabel;
                }
                else
                {
                    SelectedPath = "";
                    SelectedOriginal = "";
                    EntryTranslationDraft = "";
                    SelectedStatus = "";
                }

                TranslateCurrentEntryCommand.RaiseCanExecuteChanged();
                SaveCurrentEntryCommand.RaiseCanExecuteChanged();
                SkipCurrentEntryCommand.RaiseCanExecuteChanged();
                ConfirmCurrentEntryCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedPath
    {
        get => selectedPath;
        set => SetProperty(ref selectedPath, value);
    }

    public string SelectedOriginal
    {
        get => selectedOriginal;
        set => SetProperty(ref selectedOriginal, value);
    }

    public string EntryTranslationDraft
    {
        get => entryTranslationDraft;
        set
        {
            if (SetProperty(ref entryTranslationDraft, value))
            {
                SaveCurrentEntryCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedStatus
    {
        get => selectedStatus;
        set => SetProperty(ref selectedStatus, value);
    }

    // ── Generation mode options ──

    public IEnumerable<TranslationGenerationMode> GenerationModeOptions =>
        Enum.GetValues<TranslationGenerationMode>();

    // ── Engine configuration ──

    public IEnumerable<TranslationEngineType> EngineOptions =>
        Enum.GetValues<TranslationEngineType>();

    /// <summary>是否显示 Google Cloud（付费）配置面板——仅当选中 Google 引擎时。</summary>
    public bool IsGooglePaidConfigVisible => SelectedEngine == TranslationEngineType.Google;

    /// <summary>是否显示 Google 免费引擎提示——仅当选中 GoogleFree 引擎时。</summary>
    public bool IsGoogleFreeNoteVisible => SelectedEngine == TranslationEngineType.GoogleFree;

    public TranslationEngineType SelectedEngine
    {
        get => selectedEngine;
        set
        {
            if (SetProperty(ref selectedEngine, value))
            {
                SyncSettingsToProject();
                SaveEngineCache();
                OnPropertyChanged(nameof(IsGooglePaidConfigVisible));
                OnPropertyChanged(nameof(IsGoogleFreeNoteVisible));
            }
        }
    }

    private TranslationEngineType selectedEngine = TranslationEngineType.OpenAI;

    /// <summary>可选的目标语言（BCP-47），用于 ComboBox 快捷选择，也可手动输入。</summary>
    public IReadOnlyList<string> TargetLanguageOptions { get; } =
        new[] { "zh-CN", "zh-TW", "ja", "ko", "en", "de", "fr", "es", "ru" };

    /// <summary>目标语言（BCP-47），例如 zh-CN / zh-TW / ja / ko。翻译结果与全局词库将按此语言保存。</summary>
    public string TargetLanguage
    {
        get => targetLanguage;
        set
        {
            if (SetProperty(ref targetLanguage, value))
            {
                SyncSettingsToProject();
                SaveEngineCache();
            }
        }
    }

    public string OpenAiApiKey
    {
        get => openAiApiKey;
        set
        {
            if (SetProperty(ref openAiApiKey, value))
            {
                SyncSettingsToProject();
                SaveEngineCache();
            }
        }
    }

    public string OpenAiModel
    {
        get => openAiModel;
        set
        {
            if (SetProperty(ref openAiModel, value))
            {
                SyncSettingsToProject();
                SaveEngineCache();
            }
        }
    }

    public string OpenAiBaseUrl
    {
        get => openAiBaseUrl;
        set
        {
            if (SetProperty(ref openAiBaseUrl, value))
            {
                SyncSettingsToProject();
                SaveEngineCache();
            }
        }
    }

    public string GoogleProjectId
    {
        get => googleProjectId;
        set
        {
            if (SetProperty(ref googleProjectId, value))
            {
                SyncSettingsToProject();
                SaveEngineCache();
            }
        }
    }

    public string GoogleLocation
    {
        get => googleLocation;
        set
        {
            if (SetProperty(ref googleLocation, value))
            {
                SyncSettingsToProject();
                SaveEngineCache();
            }
        }
    }

    public string GoogleServiceAccountJsonPath
    {
        get => googleServiceAccountJsonPath;
        set
        {
            if (SetProperty(ref googleServiceAccountJsonPath, value))
            {
                SyncSettingsToProject();
                SaveEngineCache();
            }
        }
    }

    // ═══════════════════════════════════════════════
    //  Engine cache
    // ═══════════════════════════════════════════════

    /// <summary>
    /// 优先使用项目已保存的引擎设置，项目无配置时回退到全局缓存。
    /// 加载完成后将最终结果写入缓存，确保下次打开新项目时有默认值。
    /// </summary>
    private void LoadSettingsFromProjectOrCache()
    {
        if (project is null) return;

        var ps = project.ProviderSettings;
        bool projectHasSettings =
            !string.IsNullOrWhiteSpace(ps.OpenAi.ApiKey)
            || !string.IsNullOrWhiteSpace(ps.Google.ProjectId)
            || ps.PreferredEngine != TranslationEngineType.OpenAI;

        if (projectHasSettings)
        {
            // 项目已有配置 → 加载到 VM
            SelectedEngine = ps.PreferredEngine;
            OpenAiApiKey = ps.OpenAi.ApiKey;
            OpenAiModel = string.IsNullOrWhiteSpace(ps.OpenAi.Model) ? "gpt-4.1-mini" : ps.OpenAi.Model;
            OpenAiBaseUrl = string.IsNullOrWhiteSpace(ps.OpenAi.BaseUrl) ? "https://api.openai.com/v1" : ps.OpenAi.BaseUrl;
            GoogleProjectId = ps.Google.ProjectId;
            GoogleLocation = string.IsNullOrWhiteSpace(ps.Google.Location) ? "global" : ps.Google.Location;
            GoogleServiceAccountJsonPath = ps.Google.ServiceAccountJsonPath;
            TargetLanguage = string.IsNullOrWhiteSpace(ps.TargetLanguage) ? "zh-CN" : ps.TargetLanguage;
        }
        else
        {
            // 项目无配置 → 从缓存加载到 VM
            var cached = engineCache.Load();
            OpenAiApiKey = cached.OpenAi.ApiKey;
            OpenAiModel = string.IsNullOrWhiteSpace(cached.OpenAi.Model) ? "gpt-4.1-mini" : cached.OpenAi.Model;
            OpenAiBaseUrl = string.IsNullOrWhiteSpace(cached.OpenAi.BaseUrl) ? "https://api.openai.com/v1" : cached.OpenAi.BaseUrl;
            GoogleProjectId = cached.Google.ProjectId;
            GoogleLocation = string.IsNullOrWhiteSpace(cached.Google.Location) ? "global" : cached.Google.Location;
            GoogleServiceAccountJsonPath = cached.Google.ServiceAccountJsonPath;
            SelectedEngine = cached.PreferredEngine;
            TargetLanguage = string.IsNullOrWhiteSpace(cached.TargetLanguage) ? "zh-CN" : cached.TargetLanguage;
        }

        // 同步到项目对象 + 更新缓存
        SyncSettingsToProject();
        SaveEngineCache();
    }

    private void SaveEngineCache()
    {
        engineCache.Save(new TranslationProviderSettings
        {
            PreferredEngine = SelectedEngine,
            TargetLanguage = TargetLanguage,
            OpenAi = new OpenAiTranslationSettings
            {
                ApiKey = OpenAiApiKey,
                Model = OpenAiModel,
                BaseUrl = OpenAiBaseUrl
            },
            Google = new GoogleTranslationSettings
            {
                ProjectId = GoogleProjectId,
                Location = GoogleLocation,
                ServiceAccountJsonPath = GoogleServiceAccountJsonPath
            }
        });
    }

    // ═══════════════════════════════════════════════
    //  Project synchronization
    // ═══════════════════════════════════════════════

    private void SyncSettingsToProject()
    {
        if (project is null)
        {
            return;
        }

        project.ProviderSettings.PreferredEngine = SelectedEngine;
        project.ProviderSettings.TargetLanguage = TargetLanguage;
        project.ProviderSettings.OpenAi.ApiKey = OpenAiApiKey;
        project.ProviderSettings.OpenAi.Model = OpenAiModel;
        project.ProviderSettings.OpenAi.BaseUrl = OpenAiBaseUrl;
        project.ProviderSettings.Google.ProjectId = GoogleProjectId;
        project.ProviderSettings.Google.Location = GoogleLocation;
        project.ProviderSettings.Google.ServiceAccountJsonPath = GoogleServiceAccountJsonPath;
    }

    private void PopulateFilesFromProject()
    {
        Files.Clear();

        if (project is null)
        {
            return;
        }

        foreach (var fileState in project.Files)
        {
            Files.Add(new TranslationFileViewModel(fileState, OnPersistRequested));
        }

        UpdateProjectSummary();
    }

    private void RefreshEntriesForSelectedFile()
    {
        Entries.Clear();

        if (selectedFile is null)
        {
            return;
        }

        foreach (var entryState in selectedFile.State.Entries)
        {
            Entries.Add(new TranslationEntryViewModel(entryState));
        }

        entriesView?.Refresh();
    }

    private void OnPersistRequested()
    {
        // Fire-and-forget save when file selection/generation mode changes
        _ = SaveProjectSilentAsync();
    }

    private async Task SaveProjectSilentAsync()
    {
        try
        {
            if (project is not null)
            {
                await translationService.SaveProjectAsync(project, cancellationTokenSource.Token);
            }
        }
        catch
        {
            // Silently ignore auto-save failures
        }
    }

    // ═══════════════════════════════════════════════
    //  Filtering
    // ═══════════════════════════════════════════════

    private bool FilterFile(object obj)
    {
        if (obj is not TranslationFileViewModel fileVm)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(FileFilterText))
        {
            return true;
        }

        return fileVm.RelativePath.Contains(FileFilterText, StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterEntry(object obj)
    {
        // No entry-level filter for now — all entries of selected file are shown
        return obj is TranslationEntryViewModel;
    }

    private void SelectFilteredFiles()
    {
        foreach (var file in Files)
        {
            if (filesView?.Filter is Predicate<object> filter && filter(file))
            {
                file.SetIsSelected(true, notifyPersist: false);
            }
        }

        OnPersistRequested();
    }

    private void ClearFilteredFiles()
    {
        foreach (var file in Files)
        {
            if (filesView?.Filter is Predicate<object> filter && filter(file))
            {
                file.SetIsSelected(false, notifyPersist: false);
            }
        }

        OnPersistRequested();
    }

    // ═══════════════════════════════════════════════
    //  Initialization
    // ═══════════════════════════════════════════════

    private async Task InitializeProjectAsync()
    {
        IsBusy = true;
        StatusMessage = "正在加载项目...";

        try
        {
            var defaultOutputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "StarPakTranslations",
                SanitizeFileName(ProjectTitle));

            // Ensure output directory exists
            if (!string.IsNullOrWhiteSpace(OutputDirectory))
            {
                defaultOutputDir = OutputDirectory;
            }

            project = await translationService.LoadOrCreateProjectAsync(
                manifest,
                defaultOutputDir,
                cancellationTokenSource.Token);

            OutputDirectory = project.OutputDirectory;
            ProjectTitle = project.ProjectName;

            // 优先用项目已保存的引擎配置，空时才回退到缓存
            LoadSettingsFromProjectOrCache();

            PopulateFilesFromProject();

            // 若输出目录里已有翻译过的补丁，自动回填已翻译项（重复检查）。
            if (project.Files.Count > 0 && HasPatchFiles(OutputDirectory))
            {
                _ = ImportExistingAsync();
            }

            StatusMessage = $"项目加载完成 —— {project.Files.Count} 个文件，"
                + $"{project.Files.Sum(f => f.Entries.Count)} 个条目。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "操作已取消。";
        }
        catch (Exception ex)
        {
            logger.Error("Failed to initialize translation project", ex);
            StatusMessage = $"加载项目失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ═══════════════════════════════════════════════
    //  Command: BrowseOutputDirectory
    // ═══════════════════════════════════════════════

    private void BrowseOutputDirectory()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择汉化输出目录",
            SelectedPath = OutputDirectory
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            OutputDirectory = dialog.SelectedPath;

            // 填入输出目录后自动做一次“重复检查”：
            // 若目录里已有翻译过的补丁，自动回填已翻译项。
            if (project is not null && Directory.Exists(OutputDirectory))
            {
                _ = ImportExistingAsync();
            }
        }
    }

    private void OpenOutputDirectory()
    {
        if (Directory.Exists(OutputDirectory))
        {
            Process.Start("explorer.exe", OutputDirectory);
        }
    }

    // ═══════════════════════════════════════════════
    //  Command: Scan
    // ═══════════════════════════════════════════════

    private async Task ScanAsync()
    {
        if (project is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "正在扫描源文件...";

        try
        {
            await translationService.ScanAsync(
                project,
                manifest,
                new Progress<string>(msg => StatusMessage = msg),
                cancellationTokenSource.Token);

            PopulateFilesFromProject();
            SaveEngineCache();

            StatusMessage = $"扫描完成 —— {project.Files.Count} 个文件，"
                + $"{project.Files.Sum(f => f.Entries.Count)} 个可翻译条目。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "操作已取消。";
        }
        catch (Exception ex)
        {
            logger.Error("Scan failed", ex);
            StatusMessage = $"扫描失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ═══════════════════════════════════════════════
    //  Command: Translate (batch)
    // ═══════════════════════════════════════════════

    private async Task TranslateAsync()
    {
        if (project is null)
        {
            return;
        }

        SyncSettingsToProject();
        SaveEngineCache();

        IsBusy = true;
        StatusMessage = "正在批量翻译...";

        try
        {
            await translationService.TranslatePendingAsync(
                project,
                new Progress<string>(msg => StatusMessage = msg),
                cancellationTokenSource.Token);

            RefreshAllViewModels();

            StatusMessage = "批量翻译完成。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "操作已取消。";
        }
        catch (Exception ex)
        {
            logger.Error("Batch translation failed", ex);
            StatusMessage = $"批量翻译失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ═══════════════════════════════════════════════
    //  Command: Generate
    // ═══════════════════════════════════════════════

    private async Task GenerateAsync()
    {
        if (project is null)
        {
            return;
        }

        SyncSettingsToProject();
        SaveEngineCache();

        IsBusy = true;
        StatusMessage = "正在生成补丁文件...";

        try
        {
            await translationService.GenerateOutputAsync(
                project,
                manifest,
                new Progress<string>(msg => StatusMessage = msg),
                cancellationTokenSource.Token);

            StatusMessage = $"补丁生成完成！输出目录: {project.OutputDirectory}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "操作已取消。";
        }
        catch (Exception ex)
        {
            logger.Error("Patch generation failed", ex);
            StatusMessage = $"生成失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ═══════════════════════════════════════════════
    //  Command: ImportExisting (从已有补丁回填翻译)
    // ═══════════════════════════════════════════════

    private async Task ImportExistingAsync()
    {
        if (project is null)
        {
            return;
        }

        SyncSettingsToProject();
        SaveEngineCache();

        IsBusy = true;
        StatusMessage = "正在检查输出目录中的已有补丁翻译...";

        try
        {
            var imported = await translationService.ImportExistingTranslationsAsync(
                project,
                new Progress<string>(msg => StatusMessage = msg),
                cancellationTokenSource.Token);

            RefreshAllViewModels();

            if (imported > 0)
            {
                StatusMessage = $"已从输出目录回填 {imported} 条翻译，未翻译部分可继续用所选引擎翻译。";
            }
            else
            {
                StatusMessage = "输出目录中未发现可回填的已有翻译。";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "操作已取消。";
        }
        catch (Exception ex)
        {
            logger.Error("Import existing translations failed", ex);
            StatusMessage = $"导入失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ═══════════════════════════════════════════════
    //  Command: SaveProject
    // ═══════════════════════════════════════════════

    private async Task SaveProjectAsync()
    {
        if (project is null)
        {
            return;
        }

        SyncSettingsToProject();
        SaveEngineCache();

        IsBusy = true;
        StatusMessage = "正在保存项目...";

        try
        {
            await translationService.SaveProjectAsync(project, cancellationTokenSource.Token);
            StatusMessage = "项目已保存。";
        }
        catch (Exception ex)
        {
            logger.Error("Failed to save project", ex);
            StatusMessage = $"保存项目失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ═══════════════════════════════════════════════
    //  Command: TranslateCurrentEntry
    // ═══════════════════════════════════════════════

    private async Task TranslateCurrentEntryAsync()
    {
        if (project is null || SelectedEntry is null || SelectedFile is null)
        {
            return;
        }

        SyncSettingsToProject();
        SaveEngineCache();

        IsBusy = true;
        StatusMessage = "正在翻译当前条目...";

        try
        {
            var translated = await translationService.TranslateSingleAsync(
                project,
                SelectedEntry.Original,
                cancellationTokenSource.Token);

            SelectedEntry.State.Translated = translated;
            SelectedEntry.State.Status = TranslationEntryStatus.Translated;
            SelectedEntry.RefreshFromState();

            EntryTranslationDraft = translated;
            SelectedStatus = SelectedEntry.StatusLabel;

            SelectedFile.RefreshCounts();
            UpdateProjectSummary();

            StatusMessage = "当前条目翻译完成。";

            SaveCurrentEntryCommand.RaiseCanExecuteChanged();
            ConfirmCurrentEntryCommand.RaiseCanExecuteChanged();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "操作已取消。";
        }
        catch (Exception ex)
        {
            logger.Error("Single entry translation failed", ex);
            StatusMessage = $"翻译失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ═══════════════════════════════════════════════
    //  Command: SaveCurrentEntry
    // ═══════════════════════════════════════════════

    private async Task SaveCurrentEntryAsync()
    {
        if (project is null || SelectedEntry is null || SelectedFile is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(EntryTranslationDraft))
        {
            return;
        }

        SyncSettingsToProject();
        SaveEngineCache();

        // Update the entry state with the draft
        SelectedEntry.State.Translated = EntryTranslationDraft;
        SelectedEntry.State.Status = TranslationEntryStatus.Translated;
        SelectedEntry.State.IsManuallyEdited = true;
        SelectedEntry.RefreshFromState();

        SelectedStatus = SelectedEntry.StatusLabel;
        SelectedFile.RefreshCounts();

        IsBusy = true;
        StatusMessage = "正在生成单条补丁...";

        try
        {
            var patchResult = await translationService.GenerateSingleEntryPatchAsync(
                project,
                SelectedFile.State,
                SelectedEntry.State,
                cancellationTokenSource.Token);

            StatusMessage = $"补丁已生成: {patchResult}";
        }
        catch (Exception ex)
        {
            logger.Error("Single entry patch generation failed", ex);
            StatusMessage = $"保存补丁失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        ConfirmCurrentEntryCommand.RaiseCanExecuteChanged();
    }

    // ═══════════════════════════════════════════════
    //  Command: SkipCurrentEntry
    // ═══════════════════════════════════════════════

    private void SkipCurrentEntry()
    {
        if (SelectedEntry is null || SelectedFile is null)
        {
            return;
        }

        SelectedEntry.State.Status = TranslationEntryStatus.Skipped;
        SelectedEntry.RefreshFromState();
        SelectedStatus = SelectedEntry.StatusLabel;

        SelectedFile.RefreshCounts();
        UpdateProjectSummary();

        TranslateCurrentEntryCommand.RaiseCanExecuteChanged();
        SaveCurrentEntryCommand.RaiseCanExecuteChanged();
        SkipCurrentEntryCommand.RaiseCanExecuteChanged();
        ConfirmCurrentEntryCommand.RaiseCanExecuteChanged();
    }

    // ═══════════════════════════════════════════════
    //  Command: ConfirmCurrentEntry
    // ═══════════════════════════════════════════════

    private void ConfirmCurrentEntry()
    {
        if (SelectedEntry is null || SelectedFile is null)
        {
            return;
        }

        SelectedEntry.State.Status = TranslationEntryStatus.Verified;
        SelectedEntry.RefreshFromState();
        SelectedStatus = SelectedEntry.StatusLabel;

        SelectedFile.RefreshCounts();
        UpdateProjectSummary();

        ConfirmCurrentEntryCommand.RaiseCanExecuteChanged();
    }

    // ═══════════════════════════════════════════════
    //  Command: BrowseGoogleServiceAccount
    // ═══════════════════════════════════════════════

    private void BrowseGoogleServiceAccount()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 Google Service Account JSON 文件",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            GoogleServiceAccountJsonPath = dialog.FileName;
        }
    }

    // ═══════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════

    private void RefreshAllViewModels()
    {
        foreach (var file in Files)
        {
            file.RefreshCounts();
        }

        RefreshEntriesForSelectedFile();
        UpdateProjectSummary();
    }

    private void UpdateProjectSummary()
    {
        if (project is null)
        {
            ProjectSummary = "尚未加载项目。";
            return;
        }

        var totalFiles = project.Files.Count;
        var totalEntries = project.Files.Sum(f => f.Entries.Count);
        var translatedEntries = project.Files.Sum(f =>
            f.Entries.Count(e => !string.IsNullOrWhiteSpace(e.Translated)));
        var verifiedEntries = project.Files.Sum(f =>
            f.Entries.Count(e => e.Status == TranslationEntryStatus.Verified));

        ProjectSummary = $"{totalFiles} 个文件，{totalEntries} 个条目 | "
            + $"已翻译: {translatedEntries}，已确认: {verifiedEntries}";
    }

    private void RefreshCommandStates()
    {
        BrowseOutputDirectoryCommand.RaiseCanExecuteChanged();
        OpenOutputDirectoryCommand.RaiseCanExecuteChanged();
        SaveProjectCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        TranslateCommand.RaiseCanExecuteChanged();
        GenerateCommand.RaiseCanExecuteChanged();
        ImportExistingCommand.RaiseCanExecuteChanged();
        SelectFilteredFilesCommand.RaiseCanExecuteChanged();
        ClearFilteredFilesCommand.RaiseCanExecuteChanged();
        TranslateCurrentEntryCommand.RaiseCanExecuteChanged();
        SaveCurrentEntryCommand.RaiseCanExecuteChanged();
        SkipCurrentEntryCommand.RaiseCanExecuteChanged();
        ConfirmCurrentEntryCommand.RaiseCanExecuteChanged();
        BrowseGoogleServiceAccountCommand.RaiseCanExecuteChanged();
    }

    private static bool HasPatchFiles(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        return Directory.EnumerateFiles(directory, "*.patch", SearchOption.AllDirectories).Any();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);

        foreach (var ch in name)
        {
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var result = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? "Untitled" : result;
    }
}
