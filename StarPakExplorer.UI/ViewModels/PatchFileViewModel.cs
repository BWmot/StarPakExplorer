using StarPakExplorer.Application.Models;

namespace StarPakExplorer.UI.ViewModels;

public sealed class PatchFileViewModel : ViewModelBase
{
    private readonly Action selectionChanged;
    private bool isSelected;

    public PatchFileViewModel(PatchFileRecord record, Action selectionChanged)
    {
        Record = record;
        this.selectionChanged = selectionChanged;
    }

    public PatchFileRecord Record { get; }

    public string DisplayPath => Record.RelativePath;

    public string Detail => $"{Record.SizeBytes / 1024d:0.##} KB  {Record.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm}";

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
