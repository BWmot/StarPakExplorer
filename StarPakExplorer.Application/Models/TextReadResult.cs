namespace StarPakExplorer.Application.Models;

public sealed class TextReadResult
{
    public string Content { get; set; } = "";
    public bool WasTruncated { get; set; }
}
