using System.Collections.ObjectModel;
using System.Diagnostics;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;
using StarPakExplorer.UI.Commands;

namespace StarPakExplorer.UI.ViewModels;

public sealed class PatchManagerViewModel : ViewModelBase
{
    private readonly IPatchStore patchStore;
    private readonly IAppLogger logger;
    private readonly PakManifest? currentManifest;
    private readonly CancellationTokenSource cancellationTokenSource = new();

    private string statusMessage = "Loading patches...";
    private string patchRoot = "";
    private PatchSetViewModel? selectedPatchSet;
    private bool isBusy;

    public PatchManagerViewModel(IPatchStore patchStore, IAppLogger logger, PakManifest? currentManifest)
    {
        this.patchStore = patchStore;
        this.logger = logger;
        this.currentManifest = currentManifest;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        OpenRootCommand = new RelayCommand(OpenRoot, () => !string.IsNullOrWhiteSpace(PatchRoot));
        DeleteSelectedFilesCommand = new AsyncRelayCommand(DeleteSelectedFilesAsync, CanDeleteSelectedFiles);
        DeleteSelectedSetCommand = new AsyncRelayCommand(DeleteSelectedSetAsync, CanDeleteSelectedSet);

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
            }
        }
    }

    public RelayCommand OpenRootCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand DeleteSelectedFilesCommand { get; }

    public AsyncRelayCommand DeleteSelectedSetCommand { get; }

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
}
