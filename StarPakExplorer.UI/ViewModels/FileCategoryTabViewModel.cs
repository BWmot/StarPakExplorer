namespace StarPakExplorer.UI.ViewModels;

public sealed class FileCategoryTabViewModel : ViewModelBase
{
    private int count;

    public FileCategoryTabViewModel(FileCategory category, string displayName)
    {
        Category = category;
        DisplayName = displayName;
    }

    public FileCategory Category { get; }

    public string DisplayName { get; }

    public int Count
    {
        get => count;
        set => SetProperty(ref count, value);
    }
}
