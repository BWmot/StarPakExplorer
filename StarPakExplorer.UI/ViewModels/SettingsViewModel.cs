using System.IO;
using Microsoft.Win32;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;
using StarPakExplorer.UI.Commands;
using WinForms = System.Windows.Forms;

namespace StarPakExplorer.UI.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings appSettings;
    private readonly IAppSettingsStore settingsStore;
    private readonly IAppLogger logger;
    private readonly CancellationTokenSource cancellationTokenSource = new();

    private string assetUnpackerPath = "";
    private string assetPackerPath = "";
    private string pakParentDirectory = "";
    private string patchRootDirectory = "";
    private string cacheRootDirectory = "";
    private string translationRootDirectory = "";
    private string globalGlossaryPath = "";
    private string glossaryLanguagesText = "";
    private string statusMessage = "";
    private bool isSaving;

    public SettingsViewModel(AppSettings appSettings, IAppSettingsStore settingsStore, IAppLogger logger)
    {
        this.appSettings = appSettings;
        this.settingsStore = settingsStore;
        this.logger = logger;

        assetUnpackerPath = appSettings.AssetUnpackerPath;
        assetPackerPath = appSettings.AssetPackerPath;
        pakParentDirectory = appSettings.PakParentDirectory;
        patchRootDirectory = appSettings.PatchRootDirectory;
        cacheRootDirectory = appSettings.CacheRootDirectory;
        translationRootDirectory = appSettings.TranslationRootDirectory;
        globalGlossaryPath = appSettings.GlobalGlossaryPath;
        glossaryLanguagesText = string.Join(", ", appSettings.GlossaryLanguages ?? new List<string>());

        BrowseUnpackerCommand = new RelayCommand(BrowseUnpacker);
        BrowsePackerCommand = new RelayCommand(BrowsePacker);
        BrowsePakDirectoryCommand = new RelayCommand(BrowsePakDirectory);
        BrowsePatchRootCommand = new RelayCommand(BrowsePatchRoot);
        BrowseCacheRootCommand = new RelayCommand(BrowseCacheRoot);
        BrowseTranslationRootCommand = new RelayCommand(BrowseTranslationRoot);
        BrowseGlobalGlossaryCommand = new RelayCommand(BrowseGlobalGlossary);
        ImportTermBankCommand = new RelayCommand(ImportTermBank);
        ExportTermBankCommand = new RelayCommand(ExportTermBank);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    public event Action<bool?>? RequestClose;

    public string AssetUnpackerPath
    {
        get => assetUnpackerPath;
        set => SetProperty(ref assetUnpackerPath, value);
    }

    public string AssetPackerPath
    {
        get => assetPackerPath;
        set => SetProperty(ref assetPackerPath, value);
    }

    public string PakParentDirectory
    {
        get => pakParentDirectory;
        set => SetProperty(ref pakParentDirectory, value);
    }

    public string PatchRootDirectory
    {
        get => patchRootDirectory;
        set => SetProperty(ref patchRootDirectory, value);
    }

    public string CacheRootDirectory
    {
        get => cacheRootDirectory;
        set => SetProperty(ref cacheRootDirectory, value);
    }

    public string TranslationRootDirectory
    {
        get => translationRootDirectory;
        set => SetProperty(ref translationRootDirectory, value);
    }

    public string GlobalGlossaryPath
    {
        get => globalGlossaryPath;
        set => SetProperty(ref globalGlossaryPath, value);
    }

    /// <summary>
    /// Comma-separated list of glossary target languages (BCP-47 codes).
    /// e.g. "zh-CN, zh-TW, ja, ko, en". Used by the glossary window to add
    /// translations for languages other than Simplified Chinese.
    /// </summary>
    public string GlossaryLanguagesText
    {
        get => glossaryLanguagesText;
        set => SetProperty(ref glossaryLanguagesText, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        set => SetProperty(ref statusMessage, value);
    }

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

    public RelayCommand BrowseUnpackerCommand { get; }

    public RelayCommand BrowsePackerCommand { get; }

    public RelayCommand BrowsePakDirectoryCommand { get; }

    public RelayCommand BrowsePatchRootCommand { get; }

    public RelayCommand BrowseCacheRootCommand { get; }

    public RelayCommand BrowseTranslationRootCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand BrowseGlobalGlossaryCommand { get; }

    public RelayCommand ImportTermBankCommand { get; }

    public RelayCommand ExportTermBankCommand { get; }

    /// <summary>Called when user picks a file to import terms from. Set by the window code-behind.</summary>
    public Action<string>? ImportTermBankAction { get; set; }

    /// <summary>Called when user picks a file to export terms to. Set by the window code-behind.</summary>
    public Action<string>? ExportTermBankAction { get; set; }

    private void BrowseUnpacker()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select asset_unpacker.exe",
            Filter = "asset_unpacker.exe|asset_unpacker.exe|Executable|*.exe|All files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            AssetUnpackerPath = dialog.FileName;
            if (string.IsNullOrWhiteSpace(AssetPackerPath))
            {
                var derived = Path.Combine(Path.GetDirectoryName(dialog.FileName) ?? "", "asset_packer.exe");
                if (File.Exists(derived))
                {
                    AssetPackerPath = derived;
                }
            }
        }
    }

    private void BrowsePacker()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select asset_packer.exe",
            Filter = "asset_packer.exe|asset_packer.exe|Executable|*.exe|All files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            AssetPackerPath = dialog.FileName;
        }
    }

    private void BrowsePakDirectory()
    {
        var selected = BrowseForFolder("Select default PAK folder", PakParentDirectory);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            PakParentDirectory = selected;
        }
    }

    private void BrowsePatchRoot()
    {
        var selected = BrowseForFolder("Select patch folder", PatchRootDirectory);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            PatchRootDirectory = selected;
        }
    }

    private void BrowseCacheRoot()
    {
        var selected = BrowseForFolder("Select cache folder", CacheRootDirectory);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            CacheRootDirectory = selected;
        }
    }

    private void BrowseTranslationRoot()
    {
        var selected = BrowseForFolder("Select translation folder", TranslationRootDirectory);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            TranslationRootDirectory = selected;
        }
    }

    private void BrowseGlobalGlossary()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "选择全局术语库文件",
            Filter = "SQLite 数据库|*.db|旧版 JSON（将被迁移）|*.json|所有文件|*.*",
            FileName = "global_glossary.db",
            InitialDirectory = AppContext.BaseDirectory,
            OverwritePrompt = false
        };

        if (dialog.ShowDialog() == true)
        {
            GlobalGlossaryPath = dialog.FileName;
        }
    }

    private void ImportTermBank()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select term bank file",
            Filter = "Term Bank Files|*.txt|All Files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            ImportTermBankAction?.Invoke(dialog.FileName);
        }
    }

    private void ExportTermBank()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export term bank",
            Filter = "Term Bank Files|*.txt",
            FileName = "glossary_export.txt"
        };

        if (dialog.ShowDialog() == true)
        {
            ExportTermBankAction?.Invoke(dialog.FileName);
        }
    }

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            appSettings.AssetUnpackerPath = AssetUnpackerPath.Trim();
            appSettings.AssetPackerPath = AssetPackerPath.Trim();
            appSettings.PakParentDirectory = PakParentDirectory.Trim();
            appSettings.PatchRootDirectory = PatchRootDirectory.Trim();
            appSettings.CacheRootDirectory = CacheRootDirectory.Trim();
            appSettings.TranslationRootDirectory = TranslationRootDirectory.Trim();
            appSettings.GlobalGlossaryPath = GlobalGlossaryPath.Trim();
            appSettings.GlossaryLanguages = ParseLanguages(GlossaryLanguagesText);

            await settingsStore.SaveAsync(appSettings, cancellationTokenSource.Token);
            StatusMessage = "Settings saved";
            RequestClose?.Invoke(true);
        }
        catch (Exception exception)
        {
            logger.Error("Save settings failed", exception);
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BrowseForFolder(string title, string? selectedPath)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            SelectedPath = !string.IsNullOrWhiteSpace(selectedPath) && Directory.Exists(selectedPath)
                ? selectedPath
                : ""
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            return dialog.SelectedPath;
        }

        return "";
    }

    /// <summary>
    /// Parses a comma/whitespace separated language list into normalized
    /// BCP-47 codes. Falls back to ["zh-CN"] when nothing valid is entered.
    /// </summary>
    private static List<string> ParseLanguages(string text)
    {
        var codes = new List<string>();
        foreach (var part in (text ?? "").Split(new[] { ',', ';', '，', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var code = part.Trim();
            if (!string.IsNullOrWhiteSpace(code) && !codes.Contains(code, StringComparer.OrdinalIgnoreCase))
            {
                codes.Add(code);
            }
        }

        return codes.Count > 0 ? codes : new List<string> { "zh-CN" };
    }
}
