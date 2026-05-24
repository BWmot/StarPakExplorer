using System.Text;
using System.Text.RegularExpressions;

namespace StarPakExplorer.Application.Models;

public static class StarboundMarkup
{
    private static readonly Regex FormattingRegex = new(@"\^[^;\r\n]+;", RegexOptions.Compiled);

    public static bool ContainsFormatting(string? text)
    {
        return !string.IsNullOrEmpty(text) && FormattingRegex.IsMatch(text);
    }

    public static string StripFormatting(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return FormattingRegex.Replace(text, string.Empty).Trim();
    }

    public static IReadOnlyList<StarboundMarkupLine> ParseLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [new StarboundMarkupLine()];
        }

        var lines = new List<StarboundMarkupLine>();
        var currentLine = new StarboundMarkupLine();
        var currentColor = "";
        var buffer = new StringBuilder();

        void FlushBuffer()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            currentLine.Segments.Add(new StarboundMarkupSegment(buffer.ToString(), currentColor));
            buffer.Clear();
        }

        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (ch == '\r')
            {
                continue;
            }

            if (ch == '\n')
            {
                FlushBuffer();
                lines.Add(currentLine);
                currentLine = new StarboundMarkupLine();
                continue;
            }

            if (ch == '^')
            {
                var semicolonIndex = text.IndexOf(';', index + 1);
                if (semicolonIndex > index + 1)
                {
                    var code = text.Substring(index + 1, semicolonIndex - index - 1);
                    if (TryParseColorCode(code, currentColor, out var nextColor))
                    {
                        FlushBuffer();
                        currentColor = nextColor ?? "";
                        index = semicolonIndex;
                        continue;
                    }
                }
            }

            buffer.Append(ch);
        }

        FlushBuffer();
        lines.Add(currentLine);

        return lines;
    }

    private static bool TryParseColorCode(string code, string currentColor, out string? nextColor)
    {
        if (string.Equals(code, "reset", StringComparison.OrdinalIgnoreCase))
        {
            nextColor = null;
            return true;
        }

        if (code.StartsWith('#'))
        {
            nextColor = code;
            return true;
        }

        var namedColor = code.ToLowerInvariant() switch
        {
            "black" => "#000000",
            "white" => "#FFFFFF",
            "gray" => "#808080",
            "grey" => "#808080",
            "red" => "#FF4040",
            "green" => "#40C040",
            "blue" => "#4080FF",
            "yellow" => "#FFFF40",
            "orange" => "#FFB040",
            "purple" => "#C040FF",
            "pink" => "#FF80C0",
            "cyan" => "#40FFFF",
            _ => null
        };

        if (namedColor is not null)
        {
            nextColor = namedColor;
            return true;
        }

        nextColor = currentColor;
        return false;
    }
}

public sealed class StarboundMarkupLine
{
    public List<StarboundMarkupSegment> Segments { get; } = [];
}

public sealed class StarboundMarkupSegment
{
    public StarboundMarkupSegment(string text, string? foreground)
    {
        Text = text;
        Foreground = foreground;
    }

    public string Text { get; }

    public string? Foreground { get; }
}
