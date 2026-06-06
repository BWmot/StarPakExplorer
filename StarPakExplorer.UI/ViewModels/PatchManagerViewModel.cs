using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;
using StarPakExplorer.Application.Services;
using StarPakExplorer.UI.Commands;

namespace StarPakExplorer.UI.ViewModels;

public sealed class PatchManagerViewModel : ViewModelBase
{
    private readonly IPatchStore patchStore;
    private readonly IAppLogger logger;
    private readonly PakExplorerService pakExplorerService;
    private readonly AppSettings appSettings;
    private readonly PakManifest? currentManifest;
    private readonly CancellationTokenSource cancellationTokenSource = new();

    private string statusMessage = "Loading patches...";
    private string patchRoot = "";
    private PatchSetViewModel? selectedPatchSet;
    private bool isBusy;

    public PatchManagerViewModel(
        IPatchStore patchStore,
        PakExplorerService pakExplorerService,
        IAppLogger logger,
        AppSettings appSettings,
        PakManifest? currentManifest)
    {
        this.patchStore = patchStore;
        this.pakExplorerService = pakExplorerService;
        this.logger = logger;
        this.appSettings = appSettings;
        this.currentManifest = currentManifest;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        OpenRootCommand = new RelayCommand(OpenRoot, () => !string.IsNullOrWhiteSpace(PatchRoot));
        DeleteSelectedFilesCommand = new AsyncRelayCommand(DeleteSelectedFilesAsync, CanDeleteSelectedFiles);
        DeleteSelectedSetCommand = new AsyncRelayCommand(DeleteSelectedSetAsync, CanDeleteSelectedSet);
        PackSelectedSetCommand = new AsyncRelayCommand(PackSelectedSetAsync, CanPackSelectedSet);
        PackFolderCommand = new AsyncRelayCommand(PackFolderAsync, () => !IsBusy);

        _ = RefreshAsync();
    }

    public ObservableCollection<PatchSetViewModel> PatchSets { get; } = [];

    public ObservableCollection<PatchFileViewModel> Files { get; } = [];

    public string PatchRoot
    {
        get => patchRoot;
        set
        {
            if (SetProperty(ref patchRoot, value))
            {
                OpenRootCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        set => SetProperty(ref statusMessage, value);
    }

    public PatchSetViewModel? SelectedPatchSet
    {
        get => selectedPatchSet;
        set
        {
            if (SetProperty(ref selectedPatchSet, value))
            {
                _ = LoadFilesAsync();
                DeleteSelectedSetCommand.RaiseCanExecuteChanged();
                OpenRootCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        set
        {
            if (SetProperty(ref isBusy, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                DeleteSelectedFilesCommand.RaiseCanExecuteChanged();
                DeleteSelectedSetCommand.RaiseCanExecuteChanged();
                PackSelectedSetCommand.RaiseCanExecuteChanged();
                PackFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand OpenRootCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand DeleteSelectedFilesCommand { get; }

    public AsyncRelayCommand DeleteSelectedSetCommand { get; }

    public AsyncRelayCommand PackSelectedSetCommand { get; }

    public AsyncRelayCommand PackFolderCommand { get; }

    public string SelectedPatchSummary => SelectedPatchSet?.DisplayName ?? "No patch set selected";

    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            PatchRoot = patchStore.GetPatchRoot();
            var patchSets = await patchStore.GetPatchSetsAsync(100, cancellationTokenSource.Token);

            PatchSets.Clear();
            foreach (var patchSet in patchSets)
            {
                PatchSets.Add(new PatchSetViewModel(patchSet));
            }

            var preferredKey = currentManifest is null ? "" : patchStore.GetPatchKey(currentManifest);
            SelectedPatchSet = PatchSets.FirstOrDefault(item => string.Equals(item.PatchKey, preferredKey, StringComparison.OrdinalIgnoreCase))
                ?? PatchSets.FirstOrDefault();

            if (SelectedPatchSet is null)
            {
                Files.Clear();
                StatusMessage = "Patch folder is empty";
            }
            else if (currentManifest is not null && string.Equals(SelectedPatchSet.PatchKey, preferredKey, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = $"Current patch set: {SelectedPatchSet.DisplayName}";
            }
            else
            {
                StatusMessage = $"Loaded {PatchSets.Count} patch sets";
            }
        }
        catch (Exception exception)
        {
            logger.Error("Refresh patch manager failed", exception);
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadFilesAsync()
    {
        if (SelectedPatchSet is null)
        {
            Files.Clear();
            return;
        }

        try
        {
            var files = await patchStore.GetFilesAsync(SelectedPatchSet.PatchKey, cancellationTokenSource.Token);
            Files.Clear();
            foreach (var file in files)
            {
                Files.Add(new PatchFileViewModel(file, () => DeleteSelectedFilesCommand.RaiseCanExecuteChanged()));
            }

            DeleteSelectedFilesCommand.RaiseCanExecuteChanged();
            StatusMessage = $"{SelectedPatchSet.DisplayName} has {Files.Count} patch files";
        }
        catch (Exception exception)
        {
            logger.Error("Load patch files failed", exception);
            StatusMessage = exception.Message;
        }
    }

    private void OpenRoot()
    {
        if (string.IsNullOrWhiteSpace(PatchRoot))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = PatchRoot,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            logger.Error("Open patch folder failed", exception);
            StatusMessage = exception.Message;
        }
    }

    private bool CanDeleteSelectedFiles()
    {
        return !IsBusy && SelectedPatchSet is not null && Files.Any(file => file.IsSelected);
    }

    private bool CanDeleteSelectedSet()
    {
        return !IsBusy && SelectedPatchSet is not null;
    }

    private bool CanPackSelectedSet()
    {
        return !IsBusy && SelectedPatchSet is not null;
    }

    private async Task PackSelectedSetAsync()
    {
        if (SelectedPatchSet is null)
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

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出封包",
            Filter = "Starbound PAK|*.pak|All files|*.*",
            FileName = GetDefaultOutputFileName(),
            InitialDirectory = GetDefaultOutputDirectory()
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = "正在封包...";
            await pakExplorerService.PackPatchSetAsync(
                SelectedPatchSet.PatchKey,
                packerPath,
                dialog.FileName,
                new Progress<string>(message =>
                {
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        StatusMessage = message;
                    }
                }),
                cancellationTokenSource.Token);

            StatusMessage = $"Exported packed file: {dialog.FileName}";
        }
        catch (Exception exception)
        {
            logger.Error("Pack patch set failed", exception);
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

    private async Task PackFolderAsync()
    {
        var sourceDirectory = BrowseForFolder(
            "Select source folder to pack",
            GetDefaultSourceDirectory());

        if (string.IsNullOrWhiteSpace(sourceDirectory))
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

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export packed PAK",
            Filter = "Starbound PAK|*.pak|All files|*.*",
            FileName = $"{new DirectoryInfo(sourceDirectory).Name}.pak",
            InitialDirectory = Path.GetDirectoryName(sourceDirectory) ?? sourceDirectory
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = "Packing folder...";
            await pakExplorerService.PackDirectoryAsync(
                packerPath,
                sourceDirectory,
                dialog.FileName,
                new Progress<string>(message =>
                {
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        StatusMessage = message;
                    }
                }),
                cancellationTokenSource.Token);

            StatusMessage = $"Exported packed file: {dialog.FileName}";
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

    private async Task DeleteSelectedFilesAsync()
    {
        if (SelectedPatchSet is null)
        {
            return;
        }

        var selectedFiles = Files.Where(file => file.IsSelected).ToList();
        if (selectedFiles.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            foreach (var file in selectedFiles)
            {
                await patchStore.RemoveAsync(SelectedPatchSet.PatchKey, file.Record.RelativePath, cancellationTokenSource.Token);
            }

            await LoadFilesAsync();
            StatusMessage = $"Deleted {selectedFiles.Count} patch files";
        }
        catch (Exception exception)
        {
            logger.Error("Delete patch files failed", exception);
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteSelectedSetAsync()
    {
        if (SelectedPatchSet is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await patchStore.DeletePatchSetAsync(SelectedPatchSet.PatchKey, cancellationTokenSource.Token);
            IsBusy = false;
            await RefreshAsync();
            StatusMessage = "Patch set deleted";
        }
        catch (Exception exception)
        {
            logger.Error("Delete patch set failed", exception);
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
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

    private string GetDefaultOutputFileName()
    {
        if (SelectedPatchSet?.Summary.SourcePakPath is { Length: > 0 } sourcePath)
        {
            return $"{Path.GetFileNameWithoutExtension(sourcePath)}_patched.pak";
        }

        return $"{SelectedPatchSet?.PatchKey ?? "patched_mod"}.pak";
    }

    private string GetDefaultOutputDirectory()
    {
        if (SelectedPatchSet?.Summary.SourcePakPath is { Length: > 0 } sourcePath)
        {
            var directory = Path.GetDirectoryName(sourcePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        if (!string.IsNullOrWhiteSpace(appSettings.PakParentDirectory) && Directory.Exists(appSettings.PakParentDirectory))
        {
            return appSettings.PakParentDirectory;
        }

        return PatchRoot;
    }

    private string GetDefaultSourceDirectory()
    {
        if (SelectedPatchSet?.Summary.SourcePakPath is { Length: > 0 } sourcePath)
        {
            var directory = Path.GetDirectoryName(sourcePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        if (!string.IsNullOrWhiteSpace(appSettings.PakParentDirectory) && Directory.Exists(appSettings.PakParentDirectory))
        {
            return appSettings.PakParentDirectory;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    private static string BrowseForFolder(string title, string? selectedPath)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            SelectedPath = !string.IsNullOrWhiteSpace(selectedPath) && Directory.Exists(selectedPath)
                ? selectedPath
                : ""
        };

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? dialog.SelectedPath
            : "";
    }
}
