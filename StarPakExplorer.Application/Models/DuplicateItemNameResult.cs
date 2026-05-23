namespace StarPakExplorer.Application.Models;

public sealed class DuplicateItemNameResult
{
    public string ItemName { get; set; } = "";
    public List<DuplicateItemNameHit> Hits { get; set; } = [];
    public int Count => Hits.Count;
}

public sealed class DuplicateItemNameHit
{
    public string FilePath { get; set; } = "";
    public int LineNumber { get; set; }
}
