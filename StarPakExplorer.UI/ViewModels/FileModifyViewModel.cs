using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;
using StarPakExplorer.Application.Services;
using StarPakExplorer.UI.Commands;

namespace StarPakExplorer.UI.ViewModels;

public sealed class FileModifyViewModel : ViewModelBase
{
    private readonly PakExplorerService pakExplorerService;
    private readonly IFileStagingStore stagingStore;
    private readonly IAppLogger logger;
    private readonly string cacheKey;
    private readonly FileListItem fileItem;
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private string statusMessage = "正在加载...";
    private string editableText = "";
    private string selectedEncodingName = "UTF-8";
    private bool isSourceMode = true;
    private FlowDocument? previewDocument;
    private BitmapSource? previewImageSource;
    private string replacementPath = "";
    private bool isSaving;

    public FileModifyViewModel(
        string cacheKey,
        FileListItem fileItem,
        PakExplorerService pakExplorerService,
        IFileStagingStore stagingStore,
        IAppLogger logger)
    {
        this.cacheKey = cacheKey;
        this.fileItem = fileItem;
        this.pakExplorerService = pakExplorerService;
        this.stagingStore = stagingStore;
        this.logger = logger;

        EncodingOptions =
        [
            new TextEncodingChoice("UTF-8", new UTF8Encoding(false)),
            new TextEncodingChoice("UTF-8 BOM", new UTF8Encoding(true)),
            new TextEncodingChoice("UTF-16 LE", Encoding.Unicode),
            new TextEncodingChoice("UTF-16 BE", Encoding.BigEndianUnicode)
        ];

        selectedEncodingName = EncodingOptions[0].Name;
        SelectReplacementCommand = new RelayCommand(SelectReplacement);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        _ = InitializeAsync();
    }

    public event Action<bool?>? RequestClose;

    public FileListItem FileItem => fileItem;

    public ObservableCollection<TextEncodingChoice> EncodingOptions { get; }

    public RelayCommand SelectReplacementCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public RelayCommand CloseCommand { get; }

    public string Title => fileItem.DisplayPath;

    public string FileTypeText => fileItem.CategoryDisplayName;

    public bool IsTextFile => fileItem.Category == FileCategory.Text;

    public bool IsImageFile => fileItem.Category == FileCategory.Image;

    public bool IsAudioFile => fileItem.Category == FileCategory.Audio;

    public bool IsOtherFile => fileItem.Category == FileCategory.Other;

    public bool CanEditText => IsTextFile;

    public bool CanReplaceBinary => IsImageFile || IsAudioFile;

    public bool IsSourceMode
    {
        get => isSourceMode;
        set
        {
            if (SetProperty(ref isSourceMode, value))
            {
                OnPropertyChanged(nameof(IsReadingMode));
            }
        }
    }

    public bool IsReadingMode
    {
        get => !isSourceMode;
        set
        {
            if (value)
            {
                IsSourceMode = false;
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        set => SetProperty(ref statusMessage, value);
    }

    public string EditableText
    {
        get => editableText;
        set
        {
            if (SetProperty(ref editableText, value))
            {
                UpdatePreviewDocument();
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedEncodingName
    {
        get => selectedEncodingName;
        set => SetProperty(ref selectedEncodingName, value);
    }

    public FlowDocument? PreviewDocument
    {
        get => previewDocument;
        set => SetProperty(ref previewDocument, value);
    }

    public BitmapSource? PreviewImageSource
    {
        get => previewImageSource;
        set => SetProperty(ref previewImageSource, value);
    }

    public string ReplacementPath
    {
        get => replacementPath;
        set
        {
            if (SetProperty(ref replacementPath, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ModeHintText => StarboundMarkup.ContainsFormatting(EditableText)
        ? "检测到 Starbound 颜色码，可切换阅读模式查看"
        : "";

    public bool IsBusy
    {
        get => isSaving;
        set
        {
            if (SetProperty(ref isSaving, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            var preview = await pakExplorerService.GetPreviewAsync(fileItem.File, cancellationTokenSource.Token);
            StatusMessage = fileItem.DisplayPath;

            if (preview.Kind == PreviewKind.Text)
            {
                EditableText = LoadInitialText(preview.SourceContent);
                PreviewDocument = PreviewDocumentBuilder.Build(EditableText);
            }
            else if (preview.Kind == PreviewKind.Image && preview.ImageBytes is { Length: > 0 })
            {
                PreviewImageSource = CreateBitmapImage(preview.ImageBytes);
            }
            else
            {
                StatusMessage = preview.Content;
            }

            SaveCommand.RaiseCanExecuteChanged();
        }
        catch (Exception exception)
        {
            logger.Error("初始化修改窗口失败", exception);
            StatusMessage = exception.Message;
        }
    }

    private void SelectReplacement()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择替换文件",
            CheckFileExists = true,
            Filter = "All files|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            ReplacementPath = dialog.FileName;
            StatusMessage = $"已选择替换文件：{Path.GetFileName(dialog.FileName)}";
        }
    }

    private bool CanSave()
    {
        if (IsBusy)
        {
            return false;
        }

        if (IsTextFile)
        {
            return true;
        }

        return CanReplaceBinary && File.Exists(ReplacementPath);
    }

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            if (IsTextFile)
            {
                var encoding = EncodingOptions.First(option => string.Equals(option.Name, SelectedEncodingName, StringComparison.OrdinalIgnoreCase)).Encoding;
                await stagingStore.SaveTextAsync(cacheKey, fileItem.File.RelativePath, EditableText, encoding, cancellationTokenSource.Token);
                StatusMessage = "文本已保存到 staging";
            }
            else if (CanReplaceBinary)
            {
                await stagingStore.SaveReplacementAsync(cacheKey, fileItem.File.RelativePath, ReplacementPath, cancellationTokenSource.Token);
                StatusMessage = "替换文件已保存到 staging";
            }

            RequestClose?.Invoke(true);
        }
        catch (Exception exception)
        {
            logger.Error("保存修改失败", exception);
            StatusMessage = exception.Message;
            MessageBox.Show(exception.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdatePreviewDocument()
    {
        PreviewDocument = PreviewDocumentBuilder.Build(EditableText);
        OnPropertyChanged(nameof(ModeHintText));
    }

    private static string LoadInitialText(string sourceContent)
    {
        return sourceContent;
    }

    private static BitmapImage CreateBitmapImage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
