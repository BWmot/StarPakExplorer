namespace StarPakExplorer.UI.ViewModels;

public sealed class FileSectionTabViewModel : ViewModelBase
{
    private int count;

    public FileSectionTabViewModel(StarboundFileSection section, string displayName)
    {
        Section = section;
        DisplayName = displayName;
    }

    public StarboundFileSection Section { get; }

    public string DisplayName { get; }

    public int Count
    {
        get => count;
        set => SetProperty(ref count, value);
    }
}
