using System.Windows;

namespace StarPakExplorer.UI;

public partial class PackManagerWindow : Window
{
    public PackManagerWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
