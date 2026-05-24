using System.IO;
using Microsoft.Win32;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;
using StarPakExplorer.UI.Commands;
using WinForms = System.Windows.Forms;

namespace StarPakExplorer.UI.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings appSettings;
    private readonly IAppSettingsStore settingsStore;
    private readonly IAppLogger logger;
    private readonly CancellationTokenSource cancellationTokenSource = new();

    private string assetUnpackerPath = "";
    private string pakParentDirectory = "";
    private string patchRootDirectory = "";
    private string statusMessage = "";
    private bool isSaving;

    public SettingsViewModel(AppSettings appSettings, IAppSettingsStore settingsStore, IAppLogger logger)
    {
        this.appSettings = appSettings;
        this.settingsStore = settingsStore;
        this.logger = logger;

        assetUnpackerPath = appSettings.AssetUnpackerPath;
        pakParentDirectory = appSettings.PakParentDirectory;
        patchRootDirectory = appSettings.PatchRootDirectory;

        BrowseUnpackerCommand = new RelayCommand(BrowseUnpacker);
        BrowsePakDirectoryCommand = new RelayCommand(BrowsePakDirectory);
        BrowsePatchRootCommand = new RelayCommand(BrowsePatchRoot);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    public event Action<bool?>? RequestClose;

    public string AssetUnpackerPath
    {
        get => assetUnpackerPath;
        set => SetProperty(ref assetUnpackerPath, value);
    }

    public string PakParentDirectory
    {
        get => pakParentDirectory;
        set => SetProperty(ref pakParentDirectory, value);
    }

    public string PatchRootDirectory
    {
        get => patchRootDirectory;
        set => SetProperty(ref patchRootDirectory, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        set => SetProperty(ref statusMessage, value);
    }

    public bool IsBusy
    {
        get => isSaving;
        set
        {
            if (SetProperty(ref isSaving, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand BrowseUnpackerCommand { get; }

    public RelayCommand BrowsePakDirectoryCommand { get; }

    public RelayCommand BrowsePatchRootCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public RelayCommand CancelCommand { get; }

    private void BrowseUnpacker()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select asset_unpacker.exe",
            Filter = "asset_unpacker.exe|asset_unpacker.exe|Executable|*.exe|All files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            AssetUnpackerPath = dialog.FileName;
        }
    }

    private void BrowsePakDirectory()
    {
        var selected = BrowseForFolder("Select default PAK folder", PakParentDirectory);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            PakParentDirectory = selected;
        }
    }

    private void BrowsePatchRoot()
    {
        var selected = BrowseForFolder("Select patch folder", PatchRootDirectory);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            PatchRootDirectory = selected;
        }
    }

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            appSettings.AssetUnpackerPath = AssetUnpackerPath.Trim();
            appSettings.PakParentDirectory = PakParentDirectory.Trim();
            appSettings.PatchRootDirectory = PatchRootDirectory.Trim();

            await settingsStore.SaveAsync(appSettings, cancellationTokenSource.Token);
            StatusMessage = "Settings saved";
            RequestClose?.Invoke(true);
        }
        catch (Exception exception)
        {
            logger.Error("Save settings failed", exception);
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
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

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            return dialog.SelectedPath;
        }

        return "";
    }
}
