using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StarPakExplorer.UI.ViewModels;

namespace StarPakExplorer.UI;

public partial class GlossaryWindow : Window
{
    private GlossaryViewModel? viewModel;

    public GlossaryWindow(GlossaryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        this.viewModel = viewModel;
        viewModel.RequestBeginEdit += BeginEditRow;
        Closed += (_, _) =>
        {
            if (this.viewModel is { } vm)
            {
                vm.RequestBeginEdit -= BeginEditRow;
                vm.Dispose();
            }
        };
    }

    private void GlossaryGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (viewModel is null || e.EditAction == DataGridEditAction.Cancel || e.Row.Item is not GlossaryEntryRow row)
        {
            return;
        }

        // Defer until the current cell edit has updated the bound property.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(async () =>
        {
            await viewModel.CommitRowAsync(row);
        }));
    }

    private void BeginEditRow(GlossaryEntryRow row)
    {
        var index = GlossaryGrid.Items.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        GlossaryGrid.SelectedIndex = index;
        GlossaryGrid.ScrollIntoView(row);
        GlossaryGrid.CurrentCell = new DataGridCellInfo(row, GlossaryGrid.Columns[0]);
        GlossaryGrid.Focus();
        GlossaryGrid.BeginEdit();
    }
}
