namespace StarPakExplorer.Application.Models;

public sealed class SearchHit
{
    public string FilePath { get; set; } = "";
    public int LineNumber { get; set; }
    public string LineText { get; set; } = "";
}
