namespace StarPakExplorer.UI.ViewModels;

public sealed class FileExtensionFilterViewModel : ViewModelBase
{
    private readonly Action? selectionChanged;
    private bool isChecked;
    private int count;

    public FileExtensionFilterViewModel(string extension, int count, bool isChecked, Action? selectionChanged)
    {
        Extension = extension;
        this.count = count;
        this.isChecked = isChecked;
        this.selectionChanged = selectionChanged;
    }

    public string Extension { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Extension) ? "(无后缀)" : Extension;

    public string Description => $"{DisplayName} ({Count})";

    public int Count
    {
        get => count;
        set
        {
            if (SetProperty(ref count, value))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public bool IsChecked
    {
        get => isChecked;
        set
        {
            if (SetProperty(ref isChecked, value))
            {
                selectionChanged?.Invoke();
            }
        }
    }

    public void SetCheckedSilently(bool value)
    {
        if (isChecked == value)
        {
            return;
        }

        isChecked = value;
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(Description));
    }
}
