using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;
using StarPakExplorer.Application.Services;
using StarPakExplorer.UI.Commands;
using WinForms = System.Windows.Forms;

namespace StarPakExplorer.UI.ViewModels;

public sealed class PackManagerViewModel : ViewModelBase
{
    private readonly PakExplorerService pakExplorerService;
    private readonly IAppLogger logger;
    private readonly AppSettings appSettings;
    private readonly CancellationTokenSource cancellationTokenSource = new();

    private string sourceDirectory = "";
    private string outputPakPath = "";
    private string statusMessage = "Select a folder to pack.";
    private int totalFileCount;
    private int selectedFileCount;
    private bool isBusy;

    public PackManagerViewModel(PakExplorerService pakExplorerService, IAppLogger logger, AppSettings appSettings)
    {
        this.pakExplorerService = pakExplorerService;
        this.logger = logger;
        this.appSettings = appSettings;

        BrowseSourceCommand = new AsyncRelayCommand(BrowseSourceAsync, () => !IsBusy);
        RefreshTreeCommand = new AsyncRelayCommand(RefreshTreeAsync, CanRefreshTree);
        BrowseOutputCommand = new RelayCommand(BrowseOutput, CanBrowseOutput);
        ExportCommand = new AsyncRelayCommand(ExportAsync, CanExport);
        SelectAllCommand = new RelayCommand(SelectAll, CanModifySelection);
        ClearSelectionCommand = new RelayCommand(ClearSelection, CanModifySelection);
        ExpandAllCommand = new RelayCommand(() => SetExpanded(true), CanModifySelection);
        CollapseAllCommand = new RelayCommand(() => SetExpanded(false), CanModifySelection);
    }

    public ObservableCollection<PackTreeNodeViewModel> RootNodes { get; } = [];

    public AsyncRelayCommand BrowseSourceCommand { get; }

    public AsyncRelayCommand RefreshTreeCommand { get; }

    public RelayCommand BrowseOutputCommand { get; }

    public AsyncRelayCommand ExportCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand ClearSelectionCommand { get; }

    public RelayCommand ExpandAllCommand { get; }

    public RelayCommand CollapseAllCommand { get; }

    public string SourceDirectory
    {
        get => sourceDirectory;
        set
        {
            if (SetProperty(ref sourceDirectory, value))
            {
                RefreshTreeCommand.RaiseCanExecuteChanged();
                BrowseOutputCommand.RaiseCanExecuteChanged();
                ExportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string OutputPakPath
    {
        get => outputPakPath;
        set
        {
            if (SetProperty(ref outputPakPath, value))
            {
                ExportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        set => SetProperty(ref statusMessage, value);
    }

    public int TotalFileCount
    {
        get => totalFileCount;
        private set => SetProperty(ref totalFileCount, value);
    }

    public int SelectedFileCount
    {
        get => selectedFileCount;
        private set => SetProperty(ref selectedFileCount, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        set
        {
            if (SetProperty(ref isBusy, value))
            {
                BrowseSourceCommand.RaiseCanExecuteChanged();
                RefreshTreeCommand.RaiseCanExecuteChanged();
                BrowseOutputCommand.RaiseCanExecuteChanged();
                ExportCommand.RaiseCanExecuteChanged();
                SelectAllCommand.RaiseCanExecuteChanged();
                ClearSelectionCommand.RaiseCanExecuteChanged();
                ExpandAllCommand.RaiseCanExecuteChanged();
                CollapseAllCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task InitializeAsync()
    {
        var defaultDirectory = GetDefaultSourceDirectory();
        if (!string.IsNullOrWhiteSpace(defaultDirectory) && Directory.Exists(defaultDirectory))
        {
            SourceDirectory = defaultDirectory;
            await LoadTreeAsync(defaultDirectory);
        }
    }

    private bool CanRefreshTree()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(SourceDirectory) && Directory.Exists(SourceDirectory);
    }

    private bool CanBrowseOutput()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(SourceDirectory);
    }

    private bool CanExport()
    {
        return !IsBusy && RootNodes.Count > 0 && SelectedFileCount > 0;
    }

    private bool CanModifySelection()
    {
        return !IsBusy && RootNodes.Count > 0;
    }

    private async Task BrowseSourceAsync()
    {
        var selected = BrowseForFolder("Select folder to pack", GetDefaultSourceDirectory());
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        SourceDirectory = selected;
        await LoadTreeAsync(selected);
    }

    private async Task RefreshTreeAsync()
    {
        if (!CanRefreshTree())
        {
            return;
        }

        await LoadTreeAsync(SourceDirectory);
    }

    private void BrowseOutput()
    {
        if (string.IsNullOrWhiteSpace(SourceDirectory) || !Directory.Exists(SourceDirectory))
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Select output pak file",
            Filter = "Starbound PAK|*.pak|All files|*.*",
            FileName = $"{new DirectoryInfo(SourceDirectory).Name}.pak",
            InitialDirectory = Path.GetDirectoryName(SourceDirectory) ?? SourceDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            OutputPakPath = dialog.FileName;
        }
    }

    private async Task ExportAsync()
    {
        if (!CanExport())
        {
            return;
        }

        var packerPath = ResolvePackerPath();
        if (string.IsNullOrWhiteSpace(packerPath) || !File.Exists(packerPath))
        {
            StatusMessage = "Please select asset_packer.exe in Settings first.";
            System.Windows.MessageBox.Show(
                "Please select a valid asset_packer.exe in Settings first.",
                "StarPakExplorer",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputPakPath))
        {
            BrowseOutput();
        }

        if (string.IsNullOrWhiteSpace(OutputPakPath))
        {
            return;
        }

        var selectedFiles = GetSelectedRelativePaths().ToList();
        if (selectedFiles.Count == 0)
        {
            StatusMessage = "No files are selected.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = "Packing...";
            await pakExplorerService.PackSelectedFilesAsync(
                packerPath,
                SourceDirectory,
                selectedFiles,
                OutputPakPath,
                new Progress<string>(message =>
                {
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        StatusMessage = message;
                    }
                }),
                cancellationTokenSource.Token);

            StatusMessage = $"Exported packed file: {OutputPakPath}";
        }
        catch (Exception exception)
        {
            logger.Error("Pack folder failed", exception);
            StatusMessage = exception.Message;
            System.Windows.MessageBox.Show(
                exception.Message,
                "StarPakExplorer",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadTreeAsync(string directory)
    {
        if (!Directory.Exists(directory))
        {
            RootNodes.Clear();
            TotalFileCount = 0;
            SelectedFileCount = 0;
            StatusMessage = "Source folder not found.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = "Reading file tree...";
            var root = await Task.Run(() => BuildTree(directory, null), cancellationTokenSource.Token);
            RootNodes.Clear();
            RootNodes.Add(root);
            root.IsExpanded = true;
            root.IsChecked = true;

            if (string.IsNullOrWhiteSpace(OutputPakPath))
            {
                OutputPakPath = Path.Combine(
                    Path.GetDirectoryName(SourceDirectory) ?? SourceDirectory,
                    $"{new DirectoryInfo(SourceDirectory).Name}.pak");
            }

            TotalFileCount = CountFiles(root);
            UpdateSelectionSummary();
            StatusMessage = $"Loaded {TotalFileCount} files.";
        }
        catch (Exception exception)
        {
            logger.Error("Load pack tree failed", exception);
            RootNodes.Clear();
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private PackTreeNodeViewModel BuildTree(string path, PackTreeNodeViewModel? parent)
    {
        var isFolder = Directory.Exists(path);
        var displayName = parent is null
            ? new DirectoryInfo(path).Name
            : Path.GetFileName(path);

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = path;
        }

        var relativePath = parent is null
            ? ""
            : Path.GetRelativePath(SourceDirectory, path).Replace('\\', '/');

        var node = new PackTreeNodeViewModel(displayName, path, relativePath, isFolder, parent, UpdateSelectionSummary);

        if (!isFolder)
        {
            return node;
        }

        foreach (var childDirectory in Directory.GetDirectories(path).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            node.AddChild(BuildTree(childDirectory, node));
        }

        foreach (var childFile in Directory.GetFiles(path).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            node.AddChild(BuildTree(childFile, node));
        }

        return node;
    }

    private IEnumerable<string> GetSelectedRelativePaths()
    {
        foreach (var root in RootNodes)
        {
            foreach (var relativePath in root.GetCheckedFilePaths())
            {
                if (!string.IsNullOrWhiteSpace(relativePath))
                {
                    yield return relativePath;
                }
            }
        }
    }

    private void UpdateSelectionSummary()
    {
        SelectedFileCount = RootNodes.Sum(CountSelectedFiles);
        ExportCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        ClearSelectionCommand.RaiseCanExecuteChanged();
        ExpandAllCommand.RaiseCanExecuteChanged();
        CollapseAllCommand.RaiseCanExecuteChanged();
    }

    private static int CountFiles(PackTreeNodeViewModel node)
    {
        if (!node.IsFolder)
        {
            return 1;
        }

        return node.Children.Sum(CountFiles);
    }

    private static int CountSelectedFiles(PackTreeNodeViewModel node)
    {
        if (!node.IsFolder)
        {
            return node.IsChecked ? 1 : 0;
        }

        return node.Children.Sum(CountSelectedFiles);
    }

    private void SelectAll()
    {
        SetCheckedState(true);
    }

    private void ClearSelection()
    {
        SetCheckedState(false);
    }

    private void SetCheckedState(bool isChecked)
    {
        foreach (var root in RootNodes)
        {
            root.IsChecked = isChecked;
        }

        UpdateSelectionSummary();
    }

    private void SetExpanded(bool isExpanded)
    {
        foreach (var root in RootNodes)
        {
            root.MarkExpandedRecursive(isExpanded);
        }
    }

    private string ResolvePackerPath()
    {
        if (!string.IsNullOrWhiteSpace(appSettings.AssetPackerPath))
        {
            return appSettings.AssetPackerPath;
        }

        if (!string.IsNullOrWhiteSpace(appSettings.AssetUnpackerPath))
        {
            var derived = Path.Combine(Path.GetDirectoryName(appSettings.AssetUnpackerPath) ?? "", "asset_packer.exe");
            if (File.Exists(derived))
            {
                return derived;
            }
        }

        return "";
    }

    private string GetDefaultSourceDirectory()
    {
        if (!string.IsNullOrWhiteSpace(appSettings.PakParentDirectory) && Directory.Exists(appSettings.PakParentDirectory))
        {
            return appSettings.PakParentDirectory;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    private static string BrowseForFolder(string title, string? selectedPath)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            SelectedPath = !string.IsNullOrWhiteSpace(selectedPath) && Directory.Exists(selectedPath)
                ? selectedPath
                : ""
        };

        return dialog.ShowDialog() == WinForms.DialogResult.OK
            ? dialog.SelectedPath
            : "";
    }
}
