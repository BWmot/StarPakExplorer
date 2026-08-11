using System.Collections.ObjectModel;
using Microsoft.Win32;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;
using StarPakExplorer.UI.Commands;

namespace StarPakExplorer.UI.ViewModels;

/// <summary>
/// ViewModel for the in-app glossary window: browse, search, add, edit,
/// delete, import and export global translation terms backed by SQLite.
/// </summary>
public sealed class GlossaryViewModel : ViewModelBase
{
    private const int SearchLimit = 2000;
    private const string AllLanguages = "全部语言";

    private readonly IGlobalGlossaryStore store;
    private readonly IAppLogger logger;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim commitGate = new(1, 1);
    private CancellationTokenSource? debounce;

    private string searchText = "";
    private string statusMessage = "正在加载术语库...";
    private bool isBusy;
    private int totalCount;
    private int shownCount;
    private GlossaryEntryRow? selectedEntry;
    private string selectedLanguage = AllLanguages;

    public GlossaryViewModel(IGlobalGlossaryStore store, IAppLogger logger, IReadOnlyList<string>? configuredLanguages = null)
    {
        this.store = store;
        this.logger = logger;

        var codes = new List<string>();
        foreach (var lang in configuredLanguages ?? [])
        {
            var code = lang?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(code) && !codes.Contains(code, StringComparer.OrdinalIgnoreCase))
            {
                codes.Add(code);
            }
        }

        if (!codes.Contains("zh-CN", StringComparer.OrdinalIgnoreCase))
        {
            codes.Add("zh-CN");
        }

        LanguageChoices = codes;
        LanguageFilterOptions = new[] { AllLanguages }.Concat(codes).ToList();
        DefaultNewLanguage = codes.Count > 0 ? codes[0] : "zh-CN";

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        AddCommand = new AsyncRelayCommand(AddAsync, () => !IsBusy);
        SaveAllCommand = new AsyncRelayCommand(SaveAllAsync, () => !IsBusy && Entries.Count > 0);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => !IsBusy);
        DeleteAllFilteredCommand = new AsyncRelayCommand(DeleteAllFilteredAsync, () => !IsBusy && Entries.Count > 0);
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !IsBusy);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => !IsBusy);

        _ = LoadAsync();
    }

    public ObservableCollection<GlossaryEntryRow> Entries { get; } = [];

    public IReadOnlyList<GlossaryEntrySource> SourceOptions { get; } = Enum.GetValues<GlossaryEntrySource>();

    /// <summary>Language codes available for the glossary (from settings).</summary>
    public IReadOnlyList<string> LanguageChoices { get; }

    /// <summary>Filter dropdown choices: "全部语言" plus the configured codes.</summary>
    public IReadOnlyList<string> LanguageFilterOptions { get; }

    /// <summary>Language used for new entries when no filter is active.</summary>
    public string DefaultNewLanguage { get; }

    /// <summary>Raised after a new row is inserted so the window can begin editing it.</summary>
    public event Action<GlossaryEntryRow>? RequestBeginEdit;

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                _ = DebouncedSearchAsync();
            }
        }
    }

    public string SelectedLanguage
    {
        get => selectedLanguage;
        set
        {
            if (SetProperty(ref selectedLanguage, value))
            {
                _ = DebouncedSearchAsync();
            }
        }
    }

    /// <summary>The language filter as passed to the store (null = all languages).</summary>
    private string? EffectiveLanguageFilter => SelectedLanguage == AllLanguages ? null : SelectedLanguage;

    public string StatusMessage
    {
        get => statusMessage;
        set => SetProperty(ref statusMessage, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        set
        {
            if (SetProperty(ref isBusy, value))
            {
                SearchCommand.RaiseCanExecuteChanged();
                AddCommand.RaiseCanExecuteChanged();
                SaveAllCommand.RaiseCanExecuteChanged();
                DeleteSelectedCommand.RaiseCanExecuteChanged();
                DeleteAllFilteredCommand.RaiseCanExecuteChanged();
                ImportCommand.RaiseCanExecuteChanged();
                ExportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int TotalCount
    {
        get => totalCount;
        set => SetProperty(ref totalCount, value);
    }

    public int ShownCount
    {
        get => shownCount;
        set => SetProperty(ref shownCount, value);
    }

    public GlossaryEntryRow? SelectedEntry
    {
        get => selectedEntry;
        set => SetProperty(ref selectedEntry, value);
    }

    public AsyncRelayCommand SearchCommand { get; }

    public AsyncRelayCommand AddCommand { get; }

    public AsyncRelayCommand SaveAllCommand { get; }

    public AsyncRelayCommand DeleteSelectedCommand { get; }

    public AsyncRelayCommand DeleteAllFilteredCommand { get; }

    public AsyncRelayCommand ImportCommand { get; }

    public AsyncRelayCommand ExportCommand { get; }

    public async Task LoadAsync()
    {
        await SearchCoreAsync(lifetime.Token);
    }

    /// <summary>Stops pending background work. Called when the window closes.</summary>
    public void Dispose()
    {
        debounce?.Cancel();
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private async Task DebouncedSearchAsync()
    {
        debounce?.Cancel();
        var current = debounce = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try
        {
            await Task.Delay(300, current.Token);
            await SearchCoreAsync(current.Token);
        }
        catch (OperationCanceledException)
        {
            // A newer search superseded this one.
        }
        finally
        {
            if (ReferenceEquals(debounce, current))
            {
                debounce = null;
            }
        }
    }

    private async Task SearchAsync()
    {
        await SearchCoreAsync(lifetime.Token);
    }

    private async Task SearchCoreAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var entries = await store.SearchAsync(SearchText, EffectiveLanguageFilter, SearchLimit, cancellationToken);

            Entries.Clear();
            foreach (var entry in entries)
            {
                Entries.Add(new GlossaryEntryRow(entry));
            }

            TotalCount = await store.CountAsync(cancellationToken);
            ShownCount = Entries.Count;
            StatusMessage = ShownCount >= SearchLimit
                ? $"共 {TotalCount} 条，当前显示前 {ShownCount} 条（结果过多，请细化搜索）"
                : $"共 {TotalCount} 条，当前显示 {ShownCount} 条";
        }
        catch (OperationCanceledException)
        {
            // Ignore: superseded by a newer search.
        }
        catch (Exception ex)
        {
            logger.Warn($"加载术语库失败: {ex.Message}", ex);
            StatusMessage = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddAsync()
    {
        var defaultLanguage = EffectiveLanguageFilter ?? DefaultNewLanguage;
        var row = new GlossaryEntryRow(new TranslationGlossaryEntry
        {
            Source = "",
            Target = "",
            Language = defaultLanguage,
            EntrySource = GlossaryEntrySource.User,
            ModifiedAt = DateTimeOffset.Now
        })
        {
            IsNew = true
        };

        Entries.Insert(0, row);
        SelectedEntry = row;
        StatusMessage = $"新增术语（{defaultLanguage}）：填写原文与译文，编辑完单元格后自动保存。";
        ShownCount = Entries.Count;
        RequestBeginEdit?.Invoke(row);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Persists a single row. If the Source or Language was renamed, the old
    /// key <c>(OriginalSource, OriginalLanguage)</c> is removed and a new key
    /// inserted.
    /// </summary>
    public async Task CommitRowAsync(GlossaryEntryRow row)
    {
        await commitGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var source = row.Source?.Trim() ?? "";
            var target = row.Target?.Trim() ?? "";
            var language = NormalizeLanguage(row.Language);
            if (string.IsNullOrWhiteSpace(source))
            {
                StatusMessage = "原文不能为空，未保存。";
                return;
            }

            if (!row.IsNew)
            {
                var sourceChanged = !string.Equals(row.OriginalSource, source, StringComparison.OrdinalIgnoreCase);
                var languageChanged = !string.Equals(row.OriginalLanguage, language, StringComparison.OrdinalIgnoreCase);
                if (sourceChanged || languageChanged)
                {
                    await store.DeleteAsync(row.OriginalSource, row.OriginalLanguage, lifetime.Token).ConfigureAwait(true);
                }
            }

            var now = DateTimeOffset.Now;
            await store.UpsertAsync(new TranslationGlossaryEntry
            {
                Source = source,
                Target = target,
                Language = language,
                EntrySource = row.EntrySource,
                Category = string.IsNullOrWhiteSpace(row.Category) ? null : row.Category.Trim(),
                Notes = string.IsNullOrWhiteSpace(row.Notes) ? null : row.Notes.Trim(),
                ModifiedAt = now
            }, lifetime.Token).ConfigureAwait(true);

            row.MarkPersisted(source, language, now);
            TotalCount = await store.CountAsync(lifetime.Token).ConfigureAwait(true);
            StatusMessage = string.IsNullOrWhiteSpace(target)
                ? $"已保存：{source}（{language}）"
                : $"已保存：{source} → {target}（{language}）";
        }
        catch (Exception ex)
        {
            logger.Warn($"保存术语失败: {ex.Message}", ex);
            StatusMessage = $"保存失败：{ex.Message}";
        }
        finally
        {
            commitGate.Release();
        }
    }

    private async Task SaveAllAsync()
    {
        var rows = Entries.Where(r => !string.IsNullOrWhiteSpace(r.Source)).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var entries = rows.Select(r => new TranslationGlossaryEntry
            {
                Source = r.Source.Trim(),
                Target = r.Target.Trim(),
                Language = NormalizeLanguage(r.Language),
                EntrySource = r.EntrySource,
                Category = string.IsNullOrWhiteSpace(r.Category) ? null : r.Category.Trim(),
                Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim(),
                ModifiedAt = DateTimeOffset.Now
            }).ToList();

            await store.UpsertManyAsync(entries, lifetime.Token);
            foreach (var row in rows)
            {
                row.MarkPersisted(row.Source.Trim(), NormalizeLanguage(row.Language), DateTimeOffset.Now);
            }

            TotalCount = await store.CountAsync(lifetime.Token);
            StatusMessage = $"已保存全部 {rows.Count} 条。";
        }
        catch (Exception ex)
        {
            logger.Warn($"批量保存失败: {ex.Message}", ex);
            StatusMessage = $"保存失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        var row = SelectedEntry;
        if (row.IsNew)
        {
            Entries.Remove(row);
            ShownCount = Entries.Count;
            StatusMessage = "已取消新增。";
            return;
        }

        var confirmed = System.Windows.MessageBox.Show(
            $"确定删除术语“{row.Source}”吗？",
            "删除术语",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
        if (!confirmed)
        {
            return;
        }

        await DeleteRowAsync(row);
    }

    private async Task DeleteAllFilteredAsync()
    {
        var rows = Entries.Where(r => !r.IsNew).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var confirmed = System.Windows.MessageBox.Show(
            $"确定删除当前显示的 {rows.Count} 条术语吗？此操作不可撤销。",
            "批量删除",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
        if (!confirmed)
        {
            return;
        }

        IsBusy = true;
        try
        {
            foreach (var row in rows)
            {
                var source = row.Source?.Trim() ?? "";
                var language = NormalizeLanguage(row.Language);
                if (!string.IsNullOrWhiteSpace(source))
                {
                    await store.DeleteAsync(source, language, lifetime.Token);
                }
            }

            Entries.Clear();
            TotalCount = await store.CountAsync(lifetime.Token);
            ShownCount = 0;
            StatusMessage = $"已删除 {rows.Count} 条。";
        }
        catch (Exception ex)
        {
            logger.Warn($"批量删除失败: {ex.Message}", ex);
            StatusMessage = $"删除失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteRowAsync(GlossaryEntryRow row)
    {
        try
        {
            var language = NormalizeLanguage(row.Language);
            await store.DeleteAsync(row.Source, language, lifetime.Token);
            Entries.Remove(row);
            TotalCount = await store.CountAsync(lifetime.Token);
            ShownCount = Entries.Count;
            StatusMessage = $"已删除：{row.Source}（{language}）";
        }
        catch (Exception ex)
        {
            logger.Warn($"删除术语失败: {ex.Message}", ex);
            StatusMessage = $"删除失败：{ex.Message}";
        }
    }

    private static string NormalizeLanguage(string? language)
    {
        var trimmed = language?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(trimmed) ? "zh-CN" : trimmed;
    }

    private async Task ImportAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择术语库文件导入",
            Filter = "术语库文件|*.txt|所有文件|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var count = await store.ImportFromFileAsync(dialog.FileName, EffectiveLanguageFilter, lifetime.Token);
            StatusMessage = count > 0
                ? $"已导入 {count} 条新术语。"
                : "没有可导入的新术语（可能已存在）。";
            await SearchCoreAsync(lifetime.Token);
        }
        catch (Exception ex)
        {
            logger.Warn($"导入失败: {ex.Message}", ex);
            StatusMessage = $"导入失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出术语库",
            Filter = "术语库文件|*.txt",
            FileName = $"glossary_export_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await store.ExportToFileAsync(dialog.FileName, lifetime.Token);
            StatusMessage = $"已导出到：{dialog.FileName}";
        }
        catch (Exception ex)
        {
            logger.Warn($"导出失败: {ex.Message}", ex);
            StatusMessage = $"导出失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
