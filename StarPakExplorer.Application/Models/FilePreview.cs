namespace StarPakExplorer.Application.Models;

public enum PreviewKind
{
    None,
    Text,
    Image,
    Unsupported
}

public sealed class FilePreview
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public PreviewKind Kind { get; set; }
    public byte[]? ImageBytes { get; set; }
    public bool WasTruncated { get; set; }
}
