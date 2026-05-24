using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace StarPakExplorer.UI;

public static class RichTextBoxBindings
{
    public static readonly DependencyProperty BoundDocumentProperty =
        DependencyProperty.RegisterAttached(
            "BoundDocument",
            typeof(FlowDocument),
            typeof(RichTextBoxBindings),
            new PropertyMetadata(null, OnBoundDocumentChanged));

    public static FlowDocument? GetBoundDocument(DependencyObject obj)
    {
        return (FlowDocument?)obj.GetValue(BoundDocumentProperty);
    }

    public static void SetBoundDocument(DependencyObject obj, FlowDocument? value)
    {
        obj.SetValue(BoundDocumentProperty, value);
    }

    private static void OnBoundDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBox richTextBox)
        {
            return;
        }

        richTextBox.Document = e.NewValue as FlowDocument ?? new FlowDocument();
    }
}
