using StarPakExplorer.Application.Models;

namespace StarPakExplorer.UI.ViewModels;

public sealed class TranslationEntryViewModel : ViewModelBase
{
    private readonly TranslationEntryState state;

    public TranslationEntryViewModel(TranslationEntryState state)
    {
        this.state = state;
    }

    public TranslationEntryState State => state;

    public string Path => state.Path;

    public string Original => state.Original;

    public string? Translated => state.Translated;

    public TranslationEntryStatus Status => state.Status;

    public string StatusLabel => state.Status switch
    {
        TranslationEntryStatus.Pending => "待翻译",
        TranslationEntryStatus.Translated => "已翻译",
        TranslationEntryStatus.Verified => "已确认",
        TranslationEntryStatus.Skipped => "已跳过",
        TranslationEntryStatus.Failed => "失败",
        _ => "未知"
    };

    public bool IsEditable => state.Status is not TranslationEntryStatus.Skipped;

    public void RefreshFromState()
    {
        OnPropertyChanged(nameof(Translated));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(IsEditable));
    }
}
