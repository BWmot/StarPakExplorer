using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.UI.ViewModels;

namespace StarPakExplorer.UI;

public partial class MainWindow
{
    private bool isImagePanning;
    private System.Windows.Point imagePanStartPoint;
    private double imagePanStartHorizontalOffset;
    private double imagePanStartVerticalOffset;

    private ITranslationSourceReader? translationSourceReader;
    private ITranslationPatchWriter? translationPatchWriter;
    private ITranslationService? translationService;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void SetTranslationServices(
        ITranslationSourceReader sourceReader,
        ITranslationPatchWriter patchWriter,
        ITranslationService translationService)
    {
        translationSourceReader = sourceReader;
        translationPatchWriter = patchWriter;
        this.translationService = translationService;
    }

    private void OpenTranslationWindow_Click(object sender, RoutedEventArgs e)
    {
        if (translationSourceReader == null || translationPatchWriter == null || translationService == null)
        {
            System.Windows.MessageBox.Show("翻译服务未初始化。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var logger = new StarPakExplorer.Infrastructure.Logging.FileAppLogger();
        var viewModel = new TranslationViewModel(translationSourceReader, translationPatchWriter, translationService, logger);
        var window = new TranslationWindow(viewModel);
        window.Owner = this;
        window.ShowDialog();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ImagePreviewScrollViewer_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer || e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        isImagePanning = true;
        imagePanStartPoint = e.GetPosition(scrollViewer);
        imagePanStartHorizontalOffset = scrollViewer.HorizontalOffset;
        imagePanStartVerticalOffset = scrollViewer.VerticalOffset;
        scrollViewer.Cursor = System.Windows.Input.Cursors.SizeAll;
        scrollViewer.CaptureMouse();
        e.Handled = true;
    }

    private void ImagePreviewScrollViewer_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!isImagePanning || sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var currentPoint = e.GetPosition(scrollViewer);
        var horizontalOffset = imagePanStartHorizontalOffset + imagePanStartPoint.X - currentPoint.X;
        var verticalOffset = imagePanStartVerticalOffset + imagePanStartPoint.Y - currentPoint.Y;

        scrollViewer.ScrollToHorizontalOffset(horizontalOffset);
        scrollViewer.ScrollToVerticalOffset(verticalOffset);
        e.Handled = true;
    }

    private void ImagePreviewScrollViewer_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer || e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        StopImagePanning(scrollViewer);
        e.Handled = true;
    }

    private void ImagePreviewScrollViewer_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            StopImagePanning(scrollViewer);
        }
    }

    private void StopImagePanning(ScrollViewer scrollViewer)
    {
        isImagePanning = false;
        scrollViewer.Cursor = System.Windows.Input.Cursors.Arrow;
        if (scrollViewer.IsMouseCaptured)
        {
            scrollViewer.ReleaseMouseCapture();
        }
    }
}
