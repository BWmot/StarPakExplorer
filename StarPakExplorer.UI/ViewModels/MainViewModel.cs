using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;
using StarPakExplorer.Application.Services;
using StarPakExplorer.UI.Commands;

namespace StarPakExplorer.UI.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly PakExplorerService pakExplorerService;
    private readonly IAppLogger logger;
    private readonly IAppSettingsStore settingsStore;
    private readonly IPatchStore patchStore;
    private readonly ICacheRepository cacheRepository;
    private readonly AppSettings appSettings;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly ObservableCollection<FileListItem> allFiles = [];

    private PakManifest? currentManifest;
    private string assetUnpackerPath = "";
    private string selectedPakPath = "";
    private string statusMessage = "请先选择 asset_unpacker.exe 和 PAK 文件";
    private string modTitle = "未加载 Mod";
    private string metadataSummary = "";
    private string previewTitle = "预览";
    private string previewText = "选择左侧文件后显示预览。";
    private BitmapSource? previewImageSource;
    private string previewSourceText = "";
    private string previewReadingText = "";
    private PreviewTextMode currentPreviewTextMode = PreviewTextMode.Reading;
    private bool previewHasFormatting;
    private FlowDocument? previewDocument;
    private PreviewKind currentPreviewKind = PreviewKind.None;
    private double previewImageZoom = 1.0;
    private string searchKeyword = "";
    private string searchSummary = "";
    private string duplicateSummary = "";
    private string lastPakDirectory = "";
    private string cacheSummaryText = "缓存加载中...";
    private bool isBusy;
    private bool isUpdatingExtensionFilters;
    private FileSectionTabViewModel? selectedFileSection;
    private FileListItem? selectedFile;

    public MainViewModel(
        PakExplorerService pakExplorerService,
        IAppLogger logger,
        IAppSettingsStore settingsStore,
        IPatchStore patchStore,
        ICacheRepository cacheRepository,
        AppSettings appSettings)
    {
        this.pakExplorerService = pakExplorerService;
        this.logger = logger;
        this.settingsStore = settingsStore;
        this.patchStore = patchStore;
        this.cacheRepository = cacheRepository;
        this.appSettings = appSettings;

        assetUnpackerPath = appSettings.AssetUnpackerPath;
        lastPakDirectory = appSettings.PakParentDirectory;

        FileSections =
        [
            new FileSectionTabViewModel(StarboundFileSection.All, "全部"),
            new FileSectionTabViewModel(StarboundFileSection.Metadata, "元数据"),
            new FileSectionTabViewModel(StarboundFileSection.Items, "物品"),
            new FileSectionTabViewModel(StarboundFileSection.Objects, "对象"),
            new FileSectionTabViewModel(StarboundFileSection.NpcsAndMonsters, "NPC&怪物"),
            new FileSectionTabViewModel(StarboundFileSection.BiomesAndWorldgen, "世界生成"),
            new FileSectionTabViewModel(StarboundFileSection.Interface, "界面"),
            new FileSectionTabViewModel(StarboundFileSection.TexturesAndAnimation, "贴图动画"),
            new FileSectionTabViewModel(StarboundFileSection.Scripts, "脚本"),
            new FileSectionTabViewModel(StarboundFileSection.Audio, "音频"),
            new FileSectionTabViewModel(StarboundFileSection.Patch, "Patch"),
            new FileSectionTabViewModel(StarboundFileSection.Other, "其他")
        ];
        selectedFileSection = FileSections[0];

        SelectUnpackerCommand = new AsyncRelayCommand(SelectUnpackerAsync, () => !IsBusy);
        SelectPakCommand = new AsyncRelayCommand(SelectPakAsync, () => !IsBusy);
        ClearCacheCommand = new AsyncRelayCommand(ClearCacheAsync, () => !IsBusy);
        DeleteSelectedCacheCommand = new AsyncRelayCommand(DeleteSelectedCacheAsync, CanDeleteSelectedCacheEntries);
        RefreshCacheOverviewCommand = new AsyncRelayCommand(RefreshCacheOverviewAsync, () => true);
        SearchCommand = new AsyncRelayCommand(SearchAsync, CanSearch);
        ScanDuplicateItemNamesCommand = new AsyncRelayCommand(ScanDuplicateItemNamesAsync, () => !IsBusy && currentManifest is not null);
        OpenSettingsCommand = new RelayCommand(OpenSettings, () => !IsBusy);
        OpenPatchManagerCommand = new RelayCommand(OpenPatchManager, () => !IsBusy);
        OpenPackManagerCommand = new RelayCommand(OpenPackManager, () => !IsBusy);
        SelectAllExtensionsCommand = new RelayCommand(SelectAllExtensions, CanModifyExtensions);
        ClearExtensionSelectionCommand = new RelayCommand(ClearExtensionSelection, CanModifyExtensions);
        SelectAllCacheEntriesCommand = new RelayCommand(SelectAllCacheEntries, CanModifyCacheEntries);
        ClearCacheSelectionCommand = new RelayCommand(ClearCacheSelection, CanModifyCacheEntries);
        OpenModifyWindowCommand = new RelayCommand(OpenModifyWindow, CanOpenModifyWindow);
        ZoomOutImageCommand = new RelayCommand(() => PreviewImageZoom /= 1.25, () => IsImageLoaded);
        ResetImageZoomCommand = new RelayCommand(() => PreviewImageZoom = 1.0, () => IsImageLoaded);
        ZoomInImageCommand = new RelayCommand(() => PreviewImageZoom *= 1.25, () => IsImageLoaded);

        _ = RefreshCacheOverviewAsync();
    }

    public ObservableCollection<FileSectionTabViewModel> FileSections { get; }

    public ObservableCollection<FileExtensionFilterViewModel> ExtensionFilters { get; } = [];

    public ObservableCollection<FileListItem> Files { get; } = [];

    public ObservableCollection<SearchHit> SearchHits { get; } = [];

    public ObservableCollection<DuplicateItemNameViewModel> DuplicateItemNames { get; } = [];

    public ObservableCollection<CacheEntryViewModel> RecentCacheEntries { get; } = [];

    public AsyncRelayCommand SelectUnpackerCommand { get; }

    public AsyncRelayCommand SelectPakCommand { get; }

    public AsyncRelayCommand ClearCacheCommand { get; }

    public AsyncRelayCommand DeleteSelectedCacheCommand { get; }

    public AsyncRelayCommand RefreshCacheOverviewCommand { get; }

    public AsyncRelayCommand SearchCommand { get; }

    public AsyncRelayCommand ScanDuplicateItemNamesCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public RelayCommand OpenPatchManagerCommand { get; }

    public RelayCommand OpenPackManagerCommand { get; }

    public RelayCommand SelectAllExtensionsCommand { get; }

    public RelayCommand ClearExtensionSelectionCommand { get; }

    public RelayCommand SelectAllCacheEntriesCommand { get; }

    public RelayCommand ClearCacheSelectionCommand { get; }

    public RelayCommand OpenModifyWindowCommand { get; }

    public RelayCommand ZoomOutImageCommand { get; }

    public RelayCommand ResetImageZoomCommand { get; }

    public RelayCommand ZoomInImageCommand { get; }

    public string AssetUnpackerPath
    {
        get => assetUnpackerPath;
        set => SetProperty(ref assetUnpackerPath, value);
    }

    public string SelectedPakPath
    {
        get => selectedPakPath;
        set => SetProperty(ref selectedPakPath, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        set => SetProperty(ref statusMessage, value);
    }

    public string ModTitle
    {
        get => modTitle;
        set => SetProperty(ref modTitle, value);
    }

    public string MetadataSummary
    {
        get => metadataSummary;
        set => SetProperty(ref metadataSummary, value);
    }

    public string PreviewTitle
    {
        get => previewTitle;
        set => SetProperty(ref previewTitle, value);
    }

    public string PreviewText
    {
        get => previewText;
        set => SetProperty(ref previewText, value);
    }

    public FlowDocument? PreviewDocument
    {
        get => previewDocument;
        set => SetProperty(ref previewDocument, value);
    }

    public bool IsSourcePreviewMode
    {
        get => CurrentPreviewTextMode == PreviewTextMode.Source;
        set
        {
            if (value)
            {
                SetPreviewTextMode(PreviewTextMode.Source);
            }
        }
    }

    public bool IsReadingPreviewMode
    {
        get => CurrentPreviewTextMode == PreviewTextMode.Reading;
        set
        {
            if (value)
            {
                SetPreviewTextMode(PreviewTextMode.Reading);
            }
        }
    }

    public PreviewTextMode CurrentPreviewTextMode
    {
        get => currentPreviewTextMode;
        set => SetPreviewTextMode(value);
    }

    public bool PreviewModeSwitchVisible => CurrentPreviewKind == PreviewKind.Text;

    public bool IsSourceTextPreviewVisible => CurrentPreviewKind == PreviewKind.Text && CurrentPreviewTextMode == PreviewTextMode.Source;

    public bool IsReadingTextPreviewVisible => CurrentPreviewKind == PreviewKind.Text && CurrentPreviewTextMode == PreviewTextMode.Reading;

    public string PreviewModeHintText => previewHasFormatting
        ? "检测到 Starbound 颜色码"
        : "";

    public BitmapSource? PreviewImageSource
    {
        get => previewImageSource;
        set
        {
            if (SetProperty(ref previewImageSource, value))
            {
                OnPropertyChanged(nameof(PreviewImageWidth));
                OnPropertyChanged(nameof(PreviewImageHeight));
                OnPropertyChanged(nameof(PreviewImageInfoText));
                OnPropertyChanged(nameof(IsImageLoaded));
                RaiseImageCommandStates();
            }
        }
    }

    public PreviewKind CurrentPreviewKind
    {
        get => currentPreviewKind;
        set
        {
            if (!SetProperty(ref currentPreviewKind, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsTextPreviewVisible));
            OnPropertyChanged(nameof(IsImagePreviewVisible));
            OnPropertyChanged(nameof(IsUnsupportedPreviewVisible));
            OnPropertyChanged(nameof(PreviewModeSwitchVisible));
            OnPropertyChanged(nameof(PreviewModeHintText));
            OnPropertyChanged(nameof(IsSourceTextPreviewVisible));
            OnPropertyChanged(nameof(IsReadingTextPreviewVisible));
        }
    }

    public bool IsTextPreviewVisible => CurrentPreviewKind == PreviewKind.Text;

    public bool IsImagePreviewVisible => CurrentPreviewKind == PreviewKind.Image;

    public bool IsUnsupportedPreviewVisible => CurrentPreviewKind == PreviewKind.Unsupported;

    public bool IsImageLoaded => PreviewImageSource is not null;

    private void SetPreviewTextMode(PreviewTextMode mode)
    {
        if (currentPreviewTextMode == mode)
        {
            return;
        }

        currentPreviewTextMode = mode;
        OnPropertyChanged(nameof(CurrentPreviewTextMode));
        OnPropertyChanged(nameof(IsSourcePreviewMode));
        OnPropertyChanged(nameof(IsReadingPreviewMode));
        OnPropertyChanged(nameof(IsSourceTextPreviewVisible));
        OnPropertyChanged(nameof(IsReadingTextPreviewVisible));
        UpdatePreviewText();
    }

    private void UpdatePreviewText()
    {
        PreviewText = currentPreviewTextMode == PreviewTextMode.Source
            ? previewSourceText
            : previewReadingText;
    }

    public double PreviewImageZoom
    {
        get => previewImageZoom;
        set
        {
            var clamped = Math.Clamp(value, 0.1, 8.0);
            if (!SetProperty(ref previewImageZoom, clamped))
            {
                return;
            }

            OnPropertyChanged(nameof(PreviewImageWidth));
            OnPropertyChanged(nameof(PreviewImageHeight));
            OnPropertyChanged(nameof(PreviewImageInfoText));
        }
    }

    public double PreviewImageWidth => PreviewImageSource is null ? 0 : PreviewImageSource.PixelWidth * PreviewImageZoom;

    public double PreviewImageHeight => PreviewImageSource is null ? 0 : PreviewImageSource.PixelHeight * PreviewImageZoom;

    public string PreviewImageInfoText
    {
        get
        {
            if (PreviewImageSource is null)
            {
                return "";
            }

            return $"{PreviewImageSource.PixelWidth} x {PreviewImageSource.PixelHeight} px   缩放 {PreviewImageZoom * 100:0}%";
        }
    }

    public string SearchKeyword
    {
        get => searchKeyword;
        set
        {
            if (SetProperty(ref searchKeyword, value))
            {
                SearchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SearchSummary
    {
        get => searchSummary;
        set => SetProperty(ref searchSummary, value);
    }

    public string DuplicateSummary
    {
        get => duplicateSummary;
        set => SetProperty(ref duplicateSummary, value);
    }

    public string CacheSummaryText
    {
        get => cacheSummaryText;
        set => SetProperty(ref cacheSummaryText, value);
    }

    public FileSectionTabViewModel? SelectedFileSection
    {
        get => selectedFileSection;
        set
        {
            if (SetProperty(ref selectedFileSection, value))
            {
                RefreshVisibleFiles(selectFirstIfNeeded: true);
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        set
        {
            if (!SetProperty(ref isBusy, value))
            {
                return;
            }

            RaiseCommandStates();
        }
    }

    public FileListItem? SelectedFile
    {
        get => selectedFile;
        set
        {
            if (SetProperty(ref selectedFile, value))
            {
                _ = LoadPreviewAsync(value);
            }
        }
    }

    private async Task SelectUnpackerAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 Starbound asset_unpacker.exe",
            Filter = "asset_unpacker.exe|asset_unpacker.exe|Executable|*.exe|All files|*.*",
            CheckFileExists = true,
            InitialDirectory = GetExistingDirectory(Path.GetDirectoryName(AssetUnpackerPath))
        };

        if (dialog.ShowDialog() == true)
        {
            AssetUnpackerPath = dialog.FileName;
            StatusMessage = "已选择 asset_unpacker.exe";
            await SaveSettingsAsync();
        }
    }

    private async Task SelectPakAsync()
    {
        if (string.IsNullOrWhiteSpace(AssetUnpackerPath))
        {
            ShowWarning("请先选择 asset_unpacker.exe。");
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 .pak 或 contents.pak",
            Filter = "Starbound PAK|*.pak|All files|*.*",
            CheckFileExists = true,
            InitialDirectory = GetPakInitialDirectory()
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        SelectedPakPath = dialog.FileName;
        lastPakDirectory = Path.GetDirectoryName(SelectedPakPath) ?? lastPakDirectory;
        await SaveSettingsAsync();
        await LoadPakAsync();
    }

    private async Task LoadPakAsync()
    {
        await RunBusyAsync(async () =>
        {
            ClearLoadedState();

            var progress = CreateProgress();
            var result = await pakExplorerService.LoadPakAsync(
                AssetUnpackerPath,
                SelectedPakPath,
                progress,
                lifetimeCancellation.Token);

            currentManifest = result.Manifest;
            ModTitle = result.Manifest.ModName ?? "未命名 Mod";
            MetadataSummary = BuildMetadataSummary(result.Manifest);
            StatusMessage = $"{result.StatusMessage}，共 {result.Manifest.Files.Count} 个文件";

            foreach (var file in result.Manifest.Files)
            {
                allFiles.Add(new FileListItem(file));
            }

            RebuildFilters();
            UpdateSectionCounts();
        }, "加载 PAK 失败");

        RefreshVisibleFiles(selectFirstIfNeeded: true);
        await RefreshCacheOverviewAsync();
    }

    private async Task LoadPreviewAsync(FileListItem? item)
    {
        if (item is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            PreviewTitle = item.DisplayPath;
            PreviewImageSource = null;
            PreviewImageZoom = 1.0;
            PreviewDocument = null;

            var preview = await pakExplorerService.GetPreviewAsync(item.File, lifetimeCancellation.Token);
            CurrentPreviewKind = preview.Kind;
            previewSourceText = preview.SourceContent;
            previewReadingText = preview.Content;
            previewHasFormatting = StarboundMarkup.ContainsFormatting(previewSourceText) || StarboundMarkup.ContainsFormatting(previewReadingText);
            OnPropertyChanged(nameof(PreviewModeHintText));
            UpdatePreviewText();
            PreviewDocument = preview.Kind == PreviewKind.Text
                ? PreviewDocumentBuilder.Build(previewSourceText)
                : null;

            if (preview.Kind == PreviewKind.Image && preview.ImageBytes is { Length: > 0 })
            {
                PreviewImageSource = CreateBitmapImage(preview.ImageBytes);
                PreviewImageZoom = 1.0;
            }
        }, "读取预览失败");
    }

    private async Task SearchAsync()
    {
        if (currentManifest is null)
        {
            ShowWarning("请先选择并加载一个 PAK。");
            return;
        }

        await RunBusyAsync(async () =>
        {
            SearchHits.Clear();
            SearchSummary = "搜索中...";

            var hits = await pakExplorerService.SearchAsync(
                currentManifest,
                SearchKeyword,
                CreateProgress(),
                lifetimeCancellation.Token);

            foreach (var hit in hits)
            {
                SearchHits.Add(hit);
            }

            SearchSummary = $"共 {hits.Count} 条命中";
            StatusMessage = SearchSummary;
        }, "搜索失败");
    }

    private async Task ScanDuplicateItemNamesAsync()
    {
        if (currentManifest is null)
        {
            ShowWarning("请先选择并加载一个 PAK。");
            return;
        }

        await RunBusyAsync(async () =>
        {
            DuplicateItemNames.Clear();
            DuplicateSummary = "扫描中...";

            var results = await pakExplorerService.ScanDuplicateItemNamesAsync(
                currentManifest,
                CreateProgress(),
                lifetimeCancellation.Token);

            foreach (var result in results)
            {
                DuplicateItemNames.Add(new DuplicateItemNameViewModel(result));
            }

            DuplicateSummary = results.Count == 0
                ? "当前 PAK 内未发现重复 itemName"
                : $"发现 {results.Count} 个重复 itemName";
            StatusMessage = DuplicateSummary;
        }, "扫描重复 itemName 失败");
    }

    private async Task ClearCacheAsync()
    {
        await RunBusyAsync(async () =>
        {
            await pakExplorerService.ClearCacheAsync(lifetimeCancellation.Token);
            StatusMessage = "缓存已清理";
        }, "清理缓存失败");

        await RefreshCacheOverviewAsync();
    }

    private async Task DeleteSelectedCacheAsync()
    {
        var selectedEntries = RecentCacheEntries
            .Where(entry => entry.IsSelected)
            .Select(entry => entry.Summary)
            .ToList();

        if (selectedEntries.Count == 0)
        {
            return;
        }

        var selectedBytes = selectedEntries.Sum(entry => entry.CacheBytes);
        var message = $"确定删除选中的 {selectedEntries.Count} 个缓存吗？\n\n总占用：{FormatBytes(selectedBytes)}";
        var confirmed = System.Windows.MessageBox.Show(message, "StarPakExplorer", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await pakExplorerService.DeleteCacheEntriesAsync(
                selectedEntries.Select(entry => entry.CacheKey),
                lifetimeCancellation.Token);

            StatusMessage = $"已删除 {selectedEntries.Count} 个缓存";
            if (currentManifest is not null && selectedEntries.Any(entry => entry.CacheKey == currentManifest.CacheKey))
            {
                ClearLoadedState();
            }
        }, "删除缓存失败");

        await RefreshCacheOverviewAsync();
    }

    private async Task RefreshCacheOverviewAsync()
    {
        try
        {
            var overview = await pakExplorerService.GetCacheOverviewAsync(8, lifetimeCancellation.Token);
            CacheSummaryText = $"占用 {FormatBytes(overview.TotalBytes)} · {overview.EntryCount} 个缓存";

            RecentCacheEntries.Clear();
            foreach (var entry in overview.RecentEntries)
            {
                RecentCacheEntries.Add(new CacheEntryViewModel(
                    entry,
                    () => _ = OpenRecentCacheAsync(entry),
                    () => _ = OpenCacheFolderAsync(entry),
                    () => _ = DeleteCacheEntryAsync(entry),
                    RaiseCommandStates));
            }

            RaiseCommandStates();
        }
        catch (Exception exception)
        {
            logger.Error("刷新缓存概览失败", exception);
            CacheSummaryText = "缓存概览加载失败";
            RaiseCommandStates();
        }
    }

    private async Task OpenRecentCacheAsync(CacheEntrySummary summary)
    {
        if (!File.Exists(summary.PakPath))
        {
            ShowWarning($"找不到原始 PAK 文件：{summary.PakPath}");
            return;
        }

        AssetUnpackerPath = appSettings.AssetUnpackerPath;
        SelectedPakPath = summary.PakPath;
        lastPakDirectory = Path.GetDirectoryName(summary.PakPath) ?? lastPakDirectory;
        await SaveSettingsAsync();
        await LoadPakAsync();
    }

    private async Task DeleteCacheEntryAsync(CacheEntrySummary summary)
    {
        var confirmed = System.Windows.MessageBox.Show(
            $"确定删除这个缓存吗？\n\n{summary.DisplayName}\n{summary.PakPath}",
            "StarPakExplorer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await pakExplorerService.DeleteCacheEntriesAsync([summary.CacheKey], lifetimeCancellation.Token);
            StatusMessage = $"已删除缓存：{summary.DisplayName}";

            if (currentManifest is not null && string.Equals(currentManifest.CacheKey, summary.CacheKey, StringComparison.OrdinalIgnoreCase))
            {
                ClearLoadedState();
            }
        }, "删除缓存失败");

        await RefreshCacheOverviewAsync();
    }

    private async Task OpenCacheFolderAsync(CacheEntrySummary summary)
    {
        var folder = cacheRepository.GetUnpackedDirectory(summary.CacheKey);
        if (!Directory.Exists(folder))
        {
            folder = cacheRepository.GetCacheDirectory(summary.CacheKey);
        }

        if (!Directory.Exists(folder))
        {
            ShowWarning($"找不到缓存目录：{summary.DisplayName}");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = folder,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            logger.Error("打开缓存目录失败", exception);
            ShowWarning(exception.Message);
        }
    }

    private void RebuildFilters()
    {
        isUpdatingExtensionFilters = true;
        ExtensionFilters.Clear();

        var groups = allFiles
            .GroupBy(file => file.Extension, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Extension = group.Key,
                Count = group.Count(),
                Priority = StarboundFileClassifier.GetExtensionPriority(group.Key)
            })
            .OrderBy(group => group.Priority)
            .ThenByDescending(group => group.Count)
            .ThenBy(group => StarboundFileClassifier.GetExtensionDisplayName(group.Extension), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in groups)
        {
            ExtensionFilters.Add(new FileExtensionFilterViewModel(
                group.Extension,
                group.Count,
                isChecked: true,
                selectionChanged: OnExtensionSelectionChanged));
        }

        isUpdatingExtensionFilters = false;
    }

    private void OnExtensionSelectionChanged()
    {
        if (isUpdatingExtensionFilters)
        {
            return;
        }

        RefreshVisibleFiles(selectFirstIfNeeded: true);
    }

    private void SelectAllExtensions()
    {
        SetAllExtensions(true);
    }

    private void ClearExtensionSelection()
    {
        SetAllExtensions(false);
    }

    private void SetAllExtensions(bool isChecked)
    {
        isUpdatingExtensionFilters = true;
        foreach (var filter in ExtensionFilters)
        {
            filter.SetCheckedSilently(isChecked);
        }

        isUpdatingExtensionFilters = false;
        RefreshVisibleFiles(selectFirstIfNeeded: true);
    }

    private void UpdateSectionCounts()
    {
        foreach (var section in FileSections)
        {
            section.Count = section.Section switch
            {
                StarboundFileSection.All => allFiles.Count,
                _ => allFiles.Count(file => file.Section == section.Section)
            };
        }
    }

    private void RefreshVisibleFiles(bool selectFirstIfNeeded)
    {
        Files.Clear();

        var selectedSection = SelectedFileSection?.Section ?? StarboundFileSection.All;
        var selectedExtensions = ExtensionFilters
            .Where(filter => filter.IsChecked)
            .Select(filter => filter.Extension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IEnumerable<FileListItem> visibleFiles = allFiles;
        if (selectedSection != StarboundFileSection.All)
        {
            visibleFiles = visibleFiles.Where(file => file.Section == selectedSection);
        }

        if (selectedExtensions.Count > 0)
        {
            visibleFiles = visibleFiles.Where(file => selectedExtensions.Contains(file.Extension));
        }
        else
        {
            visibleFiles = Enumerable.Empty<FileListItem>();
        }

        var materialized = visibleFiles.ToList();
        foreach (var file in materialized)
        {
            Files.Add(file);
        }

        if (Files.Count == 0)
        {
            selectedFile = null;
            OnPropertyChanged(nameof(SelectedFile));
            PreviewTitle = "预览";
            PreviewText = "当前筛选条件下没有文件。";
            PreviewImageSource = null;
            PreviewImageZoom = 1.0;
            PreviewDocument = null;
            CurrentPreviewKind = PreviewKind.None;
            SetPreviewTextMode(PreviewTextMode.Reading);
            previewSourceText = "";
            previewReadingText = "当前筛选条件下没有文件。";
            previewHasFormatting = false;
            OnPropertyChanged(nameof(PreviewModeHintText));
            UpdatePreviewText();
            return;
        }

        if (selectedFile is not null && Files.Contains(selectedFile))
        {
            return;
        }

        if (selectFirstIfNeeded)
        {
            SelectedFile = Files[0];
        }
    }

    private async Task SaveSettingsAsync()
    {
        appSettings.AssetUnpackerPath = AssetUnpackerPath;
        appSettings.PakParentDirectory = lastPakDirectory;

        try
        {
            await settingsStore.SaveAsync(appSettings, lifetimeCancellation.Token);
        }
        catch (Exception exception)
        {
            logger.Error("保存配置失败", exception);
        }
    }

    private static string GetExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        return Directory.Exists(path) ? path : "";
    }

    private string GetPakInitialDirectory()
    {
        if (Directory.Exists(lastPakDirectory))
        {
            return lastPakDirectory;
        }

        var selectedPakDirectory = GetExistingDirectory(Path.GetDirectoryName(SelectedPakPath));
        if (!string.IsNullOrWhiteSpace(selectedPakDirectory))
        {
            return selectedPakDirectory;
        }

        var unpackerDirectory = GetExistingDirectory(Path.GetDirectoryName(AssetUnpackerPath));
        if (!string.IsNullOrWhiteSpace(unpackerDirectory))
        {
            return unpackerDirectory;
        }

        return "";
    }

    private async Task RunBusyAsync(Func<Task> action, string errorTitle)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            logger.Error(errorTitle, exception);
            StatusMessage = errorTitle;
            System.Windows.MessageBox.Show(exception.Message, errorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Progress<string> CreateProgress()
    {
        return new Progress<string>(message =>
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                StatusMessage = message;
            }
        });
    }

    private bool CanSearch()
    {
        return !IsBusy && currentManifest is not null && !string.IsNullOrWhiteSpace(SearchKeyword);
    }

    private bool CanModifyExtensions()
    {
        return !IsBusy && ExtensionFilters.Count > 0;
    }

    private bool CanModifyCacheEntries()
    {
        return !IsBusy && RecentCacheEntries.Count > 0;
    }

    private bool CanDeleteSelectedCacheEntries()
    {
        return !IsBusy && RecentCacheEntries.Any(entry => entry.IsSelected);
    }

    private bool CanOpenModifyWindow(object? parameter)
    {
        if (IsBusy || currentManifest is null)
        {
            return false;
        }

        return parameter is FileListItem || selectedFile is not null;
    }

    private void OpenModifyWindow(object? parameter)
    {
        var item = parameter as FileListItem ?? selectedFile;
        if (item is null || currentManifest is null)
        {
            ShowWarning("请先选择一个文件");
            return;
        }

        var window = new FileModifyWindow
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            DataContext = new FileModifyViewModel(
                currentManifest,
                item,
                pakExplorerService,
                patchStore,
                logger)
        };

        if (window.DataContext is FileModifyViewModel viewModel)
        {
            viewModel.RequestClose += result =>
            {
                window.DialogResult = result;
                window.Close();
            };
        }

        window.ShowDialog();
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            DataContext = new SettingsViewModel(appSettings, settingsStore, logger)
        };

        if (window.DataContext is SettingsViewModel viewModel)
        {
            viewModel.RequestClose += result =>
            {
                window.DialogResult = result;
                window.Close();
            };
        }

        if (window.ShowDialog() == true)
        {
            ApplySettingsSnapshot();
            StatusMessage = "设置已保存";
        }
    }

    private void OpenPatchManager()
    {
        var window = new PatchManagerWindow
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            DataContext = new PatchManagerViewModel(patchStore, pakExplorerService, logger, appSettings, currentManifest)
        };

        window.ShowDialog();
    }

    private void OpenPackManager()
    {
        var window = new PackManagerWindow
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            DataContext = new PackManagerViewModel(pakExplorerService, logger, appSettings)
        };

        if (window.DataContext is PackManagerViewModel viewModel)
        {
            _ = viewModel.InitializeAsync();
        }

        window.ShowDialog();
    }

    private void SelectAllCacheEntries()
    {
        foreach (var entry in RecentCacheEntries)
        {
            entry.IsSelected = true;
        }
    }

    private void ClearCacheSelection()
    {
        foreach (var entry in RecentCacheEntries)
        {
            entry.IsSelected = false;
        }
    }

    private void RaiseCommandStates()
    {
        SelectUnpackerCommand.RaiseCanExecuteChanged();
        SelectPakCommand.RaiseCanExecuteChanged();
        ClearCacheCommand.RaiseCanExecuteChanged();
        DeleteSelectedCacheCommand.RaiseCanExecuteChanged();
        SearchCommand.RaiseCanExecuteChanged();
        ScanDuplicateItemNamesCommand.RaiseCanExecuteChanged();
        SelectAllExtensionsCommand.RaiseCanExecuteChanged();
        ClearExtensionSelectionCommand.RaiseCanExecuteChanged();
        SelectAllCacheEntriesCommand.RaiseCanExecuteChanged();
        ClearCacheSelectionCommand.RaiseCanExecuteChanged();
        OpenModifyWindowCommand.RaiseCanExecuteChanged();
        OpenSettingsCommand.RaiseCanExecuteChanged();
        OpenPatchManagerCommand.RaiseCanExecuteChanged();
        OpenPackManagerCommand.RaiseCanExecuteChanged();
        RaiseImageCommandStates();
    }

    private void RaiseImageCommandStates()
    {
        ZoomOutImageCommand.RaiseCanExecuteChanged();
        ResetImageZoomCommand.RaiseCanExecuteChanged();
        ZoomInImageCommand.RaiseCanExecuteChanged();
    }

    private void ClearLoadedState()
    {
        currentManifest = null;
        allFiles.Clear();
        Files.Clear();
        SearchHits.Clear();
        DuplicateItemNames.Clear();
        ExtensionFilters.Clear();
        SearchSummary = "";
        DuplicateSummary = "";
        PreviewTitle = "预览";
        PreviewText = "选择左侧文件后显示预览。";
        PreviewImageSource = null;
        PreviewImageZoom = 1.0;
        PreviewDocument = null;
        CurrentPreviewKind = PreviewKind.None;
        SetPreviewTextMode(PreviewTextMode.Reading);
        previewSourceText = "";
        previewReadingText = "选择左侧文件后显示预览。";
        previewHasFormatting = false;
        OnPropertyChanged(nameof(PreviewModeHintText));
        UpdatePreviewText();
        ModTitle = "加载中...";
        MetadataSummary = "";
        selectedFile = null;
        selectedFileSection = FileSections[0];
        OnPropertyChanged(nameof(SelectedFileSection));
    }

    private void ApplySettingsSnapshot()
    {
        AssetUnpackerPath = appSettings.AssetUnpackerPath;
        lastPakDirectory = appSettings.PakParentDirectory;
    }

    private static string BuildMetadataSummary(PakManifest manifest)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(manifest.WorkshopId))
        {
            parts.Add($"Workshop ID: {manifest.WorkshopId}");
        }

        if (!string.IsNullOrWhiteSpace(manifest.Author))
        {
            parts.Add($"Author: {manifest.Author}");
        }

        parts.Add($"Files: {manifest.Files.Count}");
        return string.Join("    ", parts);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:0.##} KB";
        }

        return $"{bytes / 1024d / 1024d:0.##} MB";
    }

    private static void ShowWarning(string message)
    {
        System.Windows.MessageBox.Show(message, "StarPakExplorer", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static BitmapImage CreateBitmapImage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
