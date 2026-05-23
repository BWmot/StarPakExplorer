using System.Text.RegularExpressions;

namespace StarPakExplorer.Application.Services;

public static partial class WorkshopIdExtractor
{
    public static string? Extract(string pakPath)
    {
        var normalized = pakPath.Replace('/', '\\');
        var match = WorkshopPathRegex().Match(normalized);
        return match.Success ? match.Groups["id"].Value : null;
    }

    [GeneratedRegex(@"steamapps\\workshop\\content\\211820\\(?<id>\d+)\\contents\.pak$", RegexOptions.IgnoreCase)]
    private static partial Regex WorkshopPathRegex();
}
