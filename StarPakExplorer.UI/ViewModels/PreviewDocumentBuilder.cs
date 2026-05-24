using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.UI.ViewModels;

public static class PreviewDocumentBuilder
{
    public static FlowDocument Build(string text)
    {
        var document = new FlowDocument
        {
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 13,
            PagePadding = new Thickness(0),
            ColumnWidth = double.PositiveInfinity
        };

        var lines = StarboundMarkup.ParseLines(text);
        if (lines.Count == 0)
        {
            document.Blocks.Add(new Paragraph(new Run(""))
            {
                Margin = new Thickness(0)
            });
            return document;
        }

        foreach (var line in lines)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0)
            };

            if (line.Segments.Count == 0)
            {
                paragraph.Inlines.Add(new Run(""));
            }
            else
            {
                foreach (var segment in line.Segments)
                {
                    var run = new Run(segment.Text);
                    if (!string.IsNullOrWhiteSpace(segment.Foreground))
                    {
                        run.Foreground = CreateBrush(segment.Foreground);
                    }

                    paragraph.Inlines.Add(run);
                }
            }

            document.Blocks.Add(paragraph);
        }

        return document;
    }

    private static System.Windows.Media.Brush CreateBrush(string colorCode)
    {
        try
        {
            var brush = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(colorCode)!;
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            return brush;
        }
        catch
        {
            return System.Windows.Media.Brushes.Black;
        }
    }
}
