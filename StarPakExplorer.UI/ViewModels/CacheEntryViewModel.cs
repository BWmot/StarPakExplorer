using StarPakExplorer.Application.Models;
using StarPakExplorer.UI.Commands;

namespace StarPakExplorer.UI.ViewModels;

public sealed class CacheEntryViewModel : ViewModelBase
{
    private readonly Action selectionChanged;
    private bool isSelected;

    public CacheEntryViewModel(CacheEntrySummary summary, Action onOpen, Action onDelete, Action selectionChanged)
    {
        Summary = summary;
        OpenCommand = new RelayCommand(onOpen);
        DeleteCommand = new RelayCommand(onDelete);
        this.selectionChanged = selectionChanged;
    }

    public CacheEntrySummary Summary { get; }

    public RelayCommand OpenCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public string Title => Summary.DisplayName;

    public string Subtitle => Summary.PakPath;

    public string Detail => $"{Summary.CacheBytes / 1024d / 1024d:0.##} MB · {Summary.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm}";

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (SetProperty(ref isSelected, value))
            {
                selectionChanged();
            }
        }
    }
}
