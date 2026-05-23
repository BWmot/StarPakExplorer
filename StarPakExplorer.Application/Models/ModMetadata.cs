namespace StarPakExplorer.Application.Models;

public sealed class ModMetadata
{
    public string? Name { get; set; }
    public string? FriendlyName { get; set; }
    public string? Author { get; set; }
    public string? SteamContentId { get; set; }

    public string GetDisplayName(string? workshopId, string pakPath)
    {
        if (!string.IsNullOrWhiteSpace(FriendlyName))
        {
            return FriendlyName;
        }

        if (!string.IsNullOrWhiteSpace(Name))
        {
            return Name;
        }

        if (!string.IsNullOrWhiteSpace(workshopId))
        {
            return workshopId;
        }

        return Path.GetFileNameWithoutExtension(pakPath);
    }
}
