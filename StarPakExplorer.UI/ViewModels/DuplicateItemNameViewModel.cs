using StarPakExplorer.Application.Models;

namespace StarPakExplorer.UI.ViewModels;

public sealed class DuplicateItemNameViewModel
{
    public DuplicateItemNameViewModel(DuplicateItemNameResult result)
    {
        ItemName = result.ItemName;
        Count = result.Count;
        HitSummary = string.Join("; ", result.Hits.Select(hit => $"{hit.FilePath}:{hit.LineNumber}"));
    }

    public string ItemName { get; }

    public int Count { get; }

    public string HitSummary { get; }
}
