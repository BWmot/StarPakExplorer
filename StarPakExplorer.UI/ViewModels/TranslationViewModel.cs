using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;
using StarPakExplorer.UI.Commands;

namespace StarPakExplorer.UI.ViewModels;

public sealed class TranslationViewModel : ViewModelBase
{
    private readonly ITranslationSourceReader sourceReader;
    private readonly ITranslationPatchWriter patchWriter;
    private readonly ITranslationService translationService;
    private readonly IAppLogger logger;
    private readonly CancellationTokenSource cancellationTokenSource = new();

    private string modPath = "";
    private string outputPath = "";
    private string searchText = "";
    private string statusMessage = "请选择已解包的 Mod 目录。";
    private string originalModName = "";
    private bool isBusy;
    private bool showOnlyUntranslated;
    private TranslatableEntryViewModel? selectedEntry;
    private System.ComponentModel.ICollectionView? entriesView;

    // ── Engine configuration ──
    private string openAiApiKey = "";
    private string openAiModel = "gpt-4.1-mini";
    private string openAiBaseUrl = "https://api.openai.com/v1";
    private string googleProjectId = "";
    private string googleLocation = "global";
    private string googleServiceAccountJsonPath = "";
    private string googleGlossaryName = "";
    private TranslationEngineType selectedEngine = TranslationEngineType.OpenAI;

    // ── Minimal project for API calls ──
    private TranslationProgressDocument? apiProject;

    public TranslationViewModel(
        ITranslationSourceReader sourceReader,
        ITranslationPatchWriter patchWriter,
        ITranslationService translationService,
        IAppLogger logger)
    {
        this.sourceReader = sourceReader;
        this.patchWriter = patchWriter;
        this.translationService = translationService;
        this.logger = logger;

        Entries = new ObservableCollection<TranslatableEntryViewModel>();
        entriesView = System.Windows.Data.CollectionViewSource.GetDefaultView(Entries);
        entriesView.Filter = FilterEntry;

        Metadata = new TranslationModMetadata();

        LoadModCommand = new AsyncRelayCommand(LoadModAsync, () => !IsBusy);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => !IsBusy && Entries.Count > 0);
        BrowseModPathCommand = new RelayCommand(BrowseModPath, () => !IsBusy);
        BrowseOutputPathCommand = new RelayCommand(BrowseOutputPath, () => !IsBusy);
        ClearTranslationsCommand = new RelayCommand(ClearAllEntries, () => !IsBusy);
        TranslateCurrentEntryCommand = new AsyncRelayCommand(TranslateCurrentEntryAsync, () => !IsBusy && SelectedEntry is not null);
        BatchTranslateCommand = new AsyncRelayCommand(BatchTranslateAsync, () => !IsBusy && Entries.Count > 0);
        BrowseGoogleServiceAccountCommand = new RelayCommand(BrowseGoogleServiceAccount, () => !IsBusy);
    }

    public ObservableCollection<TranslatableEntryViewModel> Entries { get; }

    public TranslationModMetadata Metadata { get; }

    public AsyncRelayCommand LoadModCommand { get; }

    public AsyncRelayCommand ExportCommand { get; }

    public RelayCommand BrowseModPathCommand { get; }

    public RelayCommand BrowseOutputPathCommand { get; }

    public RelayCommand ClearTranslationsCommand { get; }

    public AsyncRelayCommand TranslateCurrentEntryCommand { get; }

    public AsyncRelayCommand BatchTranslateCommand { get; }

    public RelayCommand BrowseGoogleServiceAccountCommand { get; }

    public string ModPath
    {
        get => modPath;
        set => SetProperty(ref modPath, value);
    }

    public string OutputPath
    {
        get => outputPath;
        set => SetProperty(ref outputPath, value);
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                RefreshView();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        set => SetProperty(ref statusMessage, value);
    }

    public string OriginalModName
    {
        get => originalModName;
        set => SetProperty(ref originalModName, value);
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

    // ── Engine Configuration ──

    public IEnumerable<TranslationEngineType> EngineOptions => Enum.GetValues<TranslationEngineType>();

    public TranslationEngineType SelectedEngine
    {
        get => selectedEngine;
        set => SetProperty(ref selectedEngine, value);
    }

    public string OpenAiApiKey
    {
        get => openAiApiKey;
        set => SetProperty(ref openAiApiKey, value);
    }

    public string OpenAiModel
    {
        get => openAiModel;
        set => SetProperty(ref openAiModel, value);
    }

    public string OpenAiBaseUrl
    {
        get => openAiBaseUrl;
        set => SetProperty(ref openAiBaseUrl, value);
    }

    public string GoogleProjectId
    {
        get => googleProjectId;
        set => SetProperty(ref googleProjectId, value);
    }

    public string GoogleLocation
    {
        get => googleLocation;
        set => SetProperty(ref googleLocation, value);
    }

    public string GoogleServiceAccountJsonPath
    {
        get => googleServiceAccountJsonPath;
        set => SetProperty(ref googleServiceAccountJsonPath, value);
    }

    public string GoogleGlossaryName
    {
        get => googleGlossaryName;
        set => SetProperty(ref googleGlossaryName, value);
    }

    public bool ShowOnlyUntranslated
    {
        get => showOnlyUntranslated;
        set
        {
            if (SetProperty(ref showOnlyUntranslated, value))
            {
                RefreshView();
            }
        }
    }

    public TranslatableEntryViewModel? SelectedEntry
    {
        get => selectedEntry;
        set
        {
            if (SetProperty(ref selectedEntry, value))
            {
                TranslateCurrentEntryCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int TranslatedCount => Entries.Count(e => e.IsTranslated);

    public int TotalCount => Entries.Count;

    public string ProgressText => $"{TranslatedCount} / {TotalCount} 已翻译";

    private bool FilterEntry(object obj)
    {
        if (obj is not TranslatableEntryViewModel entryVm)
        {
            return false;
        }

        if (ShowOnlyUntranslated && entryVm.IsTranslated)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var searchLower = SearchText.ToLowerInvariant();
            if (!entryVm.ItemName.ToLowerInvariant().Contains(searchLower) &&
                !entryVm.RelativePath.ToLowerInvariant().Contains(searchLower))
            {
                return false;
            }
        }

        return true;
    }

    private void RefreshView()
    {
        entriesView?.Refresh();
        OnPropertyChanged(nameof(TranslatedCount));
        OnPropertyChanged(nameof(ProgressText));
    }

    private async Task LoadModAsync()
    {
        if (string.IsNullOrWhiteSpace(ModPath) || !Directory.Exists(ModPath))
        {
            StatusMessage = "Mod 目录不存在，请先选择有效目录。";
            return;
        }

        IsBusy = true;
        StatusMessage = "正在扫描可翻译文件...";

        try
        {
            var entries = await sourceReader.ReadEntriesAsync(
                ModPath,
                cancellationTokenSource.Token);

            Entries.Clear();

            foreach (var entry in entries)
            {
                Entries.Add(new TranslatableEntryViewModel(entry));
            }

            StatusMessage = $"加载完成，共 {Entries.Count} 个可翻译条目。";

            // Try to read original mod name from _metadata
            TryReadOriginalModName();

            OnPropertyChanged(nameof(TranslatedCount));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(ProgressText));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "操作已取消。";
        }
        catch (Exception ex)
        {
            logger.Error("Failed to load mod for translation", ex);
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void TryReadOriginalModName()
    {
        var metadataPath = Path.Combine(ModPath, "_metadata");
        if (File.Exists(metadataPath))
        {
            try
            {
                var text = File.ReadAllText(metadataPath);
                using var doc = System.Text.Json.JsonDocument.Parse(text,
                    new System.Text.Json.JsonDocumentOptions
                    {
                        CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });

                if (doc.RootElement.TryGetProperty("name", out var nameProp))
                {
                    OriginalModName = nameProp.GetString() ?? "";
                }
            }
            catch
            {
                // Ignore metadata read errors
            }
        }
    }

    private async Task ExportAsync()
    {
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            StatusMessage = "请先选择输出目录。";
            return;
        }

        if (string.IsNullOrWhiteSpace(Metadata.ModName))
        {
            StatusMessage = "请在元数据中填写 Mod 名称。";
            return;
        }

        if (string.IsNullOrWhiteSpace(OriginalModName))
        {
            StatusMessage = "未能识别原 Mod 名称，请手动填写。";
            return;
        }

        IsBusy = true;
        StatusMessage = "正在生成翻译 Mod...";

        try
        {
            var entries = Entries.Select(e => e.Entry).ToList();

            await patchWriter.WriteTranslationModAsync(
                OutputPath,
                entries,
                Metadata,
                OriginalModName,
                cancellationTokenSource.Token);

            StatusMessage = $"导出完成！输出目录: {OutputPath}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "操作已取消。";
        }
        catch (Exception ex)
        {
            logger.Error("Failed to export translation mod", ex);
            StatusMessage = $"导出失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BrowseModPath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择已解包的 Mod 目录"
        };

        if (dialog.ShowDialog() == true)
        {
            ModPath = dialog.FolderName;
        }
    }

    private void BrowseOutputPath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择翻译 Mod 输出目录"
        };

        if (dialog.ShowDialog() == true)
        {
            OutputPath = dialog.FolderName;
        }
    }

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

    private void RefreshCommandStates()
    {
        LoadModCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
        BrowseModPathCommand.RaiseCanExecuteChanged();
        BrowseOutputPathCommand.RaiseCanExecuteChanged();
        ClearTranslationsCommand.RaiseCanExecuteChanged();
        TranslateCurrentEntryCommand.RaiseCanExecuteChanged();
        BatchTranslateCommand.RaiseCanExecuteChanged();
        BrowseGoogleServiceAccountCommand.RaiseCanExecuteChanged();
    }

    private TranslationProgressDocument GetOrCreateProject()
    {
        if (apiProject is not null)
        {
            return apiProject;
        }

        var projectKey = string.IsNullOrWhiteSpace(ModPath)
            ? "translation_refine"
            : $"translation_refine_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ModPath)))[..12]}";

        apiProject = new TranslationProgressDocument
        {
            ProjectKey = projectKey,
            ProjectName = "翻译精修",
            ProviderSettings = new TranslationProviderSettings
            {
                PreferredEngine = SelectedEngine,
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
                    ServiceAccountJsonPath = GoogleServiceAccountJsonPath,
                    GlossaryName = GoogleGlossaryName
                }
            }
        };

        return apiProject;
    }

    private void SyncSettingsToProject()
    {
        if (apiProject is null)
        {
            return;
        }

        apiProject.ProviderSettings.PreferredEngine = SelectedEngine;
        apiProject.ProviderSettings.OpenAi.ApiKey = OpenAiApiKey;
        apiProject.ProviderSettings.OpenAi.Model = OpenAiModel;
        apiProject.ProviderSettings.OpenAi.BaseUrl = OpenAiBaseUrl;
        apiProject.ProviderSettings.Google.ProjectId = GoogleProjectId;
        apiProject.ProviderSettings.Google.Location = GoogleLocation;
        apiProject.ProviderSettings.Google.ServiceAccountJsonPath = GoogleServiceAccountJsonPath;
        apiProject.ProviderSettings.Google.GlossaryName = GoogleGlossaryName;
    }

    private async Task TranslateCurrentEntryAsync()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "正在翻译当前条目...";

        try
        {
            var project = GetOrCreateProject();
            SyncSettingsToProject();

            foreach (var field in SelectedEntry.FieldViewModels)
            {
                if (string.IsNullOrWhiteSpace(field.OriginalValue))
                {
                    continue;
                }

                // Skip already translated fields (user can clear them manually)
                if (!string.IsNullOrWhiteSpace(field.TranslatedValue) &&
                    !string.Equals(field.TranslatedValue, field.OriginalValue, StringComparison.Ordinal))
                {
                    continue;
                }

                cancellationTokenSource.Token.ThrowIfCancellationRequested();

                var translated = await translationService.TranslateSingleAsync(
                    project,
                    field.OriginalValue,
                    cancellationTokenSource.Token);

                if (!string.IsNullOrWhiteSpace(translated))
                {
                    field.TranslatedValue = translated;
                }
            }

            StatusMessage = $"已翻译条目: {SelectedEntry.ItemName}";
            RefreshView();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "翻译已取消。";
        }
        catch (Exception ex)
        {
            logger.Error("Failed to translate current entry", ex);
            StatusMessage = $"翻译失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BatchTranslateAsync()
    {
        if (Entries.Count == 0)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "正在批量翻译...";

        try
        {
            var project = GetOrCreateProject();
            SyncSettingsToProject();

            var untranslatedFields = new List<(TranslatableEntryViewModel Entry, TranslationFieldViewModel Field)>();

            foreach (var entry in Entries)
            {
                foreach (var field in entry.FieldViewModels)
                {
                    if (string.IsNullOrWhiteSpace(field.OriginalValue))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(field.TranslatedValue) &&
                        !string.Equals(field.TranslatedValue, field.OriginalValue, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    untranslatedFields.Add((entry, field));
                }
            }

            if (untranslatedFields.Count == 0)
            {
                StatusMessage = "没有需要翻译的内容。";
                return;
            }

            var total = untranslatedFields.Count;
            var completed = 0;

            foreach (var (_, field) in untranslatedFields)
            {
                cancellationTokenSource.Token.ThrowIfCancellationRequested();

                StatusMessage = $"正在翻译... ({completed + 1}/{total})";

                var translated = await translationService.TranslateSingleAsync(
                    project,
                    field.OriginalValue,
                    cancellationTokenSource.Token);

                if (!string.IsNullOrWhiteSpace(translated))
                {
                    field.TranslatedValue = translated;
                }

                completed++;
            }

            StatusMessage = $"批量翻译完成: {completed}/{total} 条已处理";
            RefreshView();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "批量翻译已取消。";
        }
        catch (Exception ex)
        {
            logger.Error("Failed to batch translate", ex);
            StatusMessage = $"批量翻译失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearAllEntries()
    {
        foreach (var entry in Entries)
        {
            foreach (var field in entry.FieldViewModels)
            {
                field.TranslatedValue = string.Empty;
            }
        }

        RefreshView();
    }
}
