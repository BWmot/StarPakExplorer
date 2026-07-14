using System.Windows;
using StarPakExplorer.UI.ViewModels;

namespace StarPakExplorer.UI;

public partial class TranslationWindow : Window
{
    public TranslationWindow(TranslationViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
