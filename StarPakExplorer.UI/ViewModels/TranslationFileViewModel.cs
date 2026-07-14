using StarPakExplorer.Application.Models;

namespace StarPakExplorer.UI.ViewModels;

public sealed class TranslationFileViewModel : ViewModelBase
{
    private readonly Action? persistRequested;
    private readonly TranslationFileState state;

    public TranslationFileViewModel(TranslationFileState state, Action? persistRequested = null)
    {
        this.state = state;
        this.persistRequested = persistRequested;
    }

    public TranslationFileState State => state;

    public string RelativePath => state.RelativePath;

    public string SuggestedMode => state.SuggestedMode;

    public bool IsSelected
    {
        get => state.IsSelected;
        set
        {
            SetIsSelected(value);
        }
    }

    public void SetIsSelected(bool value, bool notifyPersist = true)
    {
        if (state.IsSelected == value)
        {
            return;
        }

        state.IsSelected = value;
        OnPropertyChanged(nameof(IsSelected));

        if (notifyPersist)
        {
            persistRequested?.Invoke();
        }
    }

    public TranslationGenerationMode GenerationMode
    {
        get => state.GenerationMode;
        set
        {
            if (state.GenerationMode == value)
            {
                return;
            }

            state.GenerationMode = value;
            OnPropertyChanged();
            persistRequested?.Invoke();
        }
    }

    public int EntryCount => state.Entries.Count;

    public int TranslatedCount => state.Entries.Count(entry => !string.IsNullOrWhiteSpace(entry.Translated));

    public int PendingCount => state.Entries.Count(entry => string.IsNullOrWhiteSpace(entry.Translated));

    public string? LastError => state.LastError;

    public string ModeLabel => state.GenerationMode == TranslationGenerationMode.Auto
        ? $"Auto ({state.SuggestedMode})"
        : state.GenerationMode.ToString();

    public void RefreshCounts()
    {
        OnPropertyChanged(nameof(EntryCount));
        OnPropertyChanged(nameof(TranslatedCount));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(LastError));
    }
}
