using StarPakExplorer.Application.Models;

namespace StarPakExplorer.UI.ViewModels;

public sealed class PatchSetViewModel : ViewModelBase
{
    private bool isSelected;

    public PatchSetViewModel(PatchSetSummary summary)
    {
        Summary = summary;
    }

    public PatchSetSummary Summary { get; }

    public string PatchKey => Summary.PatchKey;

    public string DisplayName => Summary.DisplayName;

    public string Subtitle => !string.IsNullOrWhiteSpace(Summary.WorkshopId)
        ? $"Workshop ID: {Summary.WorkshopId}"
        : (Summary.SourcePakPath ?? "");

    public string Detail => $"{Summary.FileCount} files  {Summary.TotalBytes / 1024d:0.##} KB  {Summary.UpdatedAt.LocalDateTime:yyyy-MM-dd HH:mm}";

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
