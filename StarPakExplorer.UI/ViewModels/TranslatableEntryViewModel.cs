using System.Collections.ObjectModel;
using System.ComponentModel;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.UI.ViewModels;

/// <summary>
/// 包装 TranslatableEntry，为每个翻译字段提供 ObservableProperty 绑定。
/// </summary>
public sealed class TranslatableEntryViewModel : ViewModelBase
{
    private readonly TranslatableEntry entry;
    private bool isTranslated;

    public TranslatableEntryViewModel(TranslatableEntry entry)
    {
        this.entry = entry;

        // Initialize translation fields
        foreach (var field in entry.SourceFields)
        {
            string translatedValue = string.Empty;
            if (entry.TranslatedFields.TryGetValue(field.Key, out var existing))
            {
                translatedValue = existing;
            }

            FieldViewModels.Add(new TranslationFieldViewModel(field.Key, field.Value, translatedValue));
        }

        // Subscribe to field changes
        foreach (var fieldVm in FieldViewModels)
        {
            fieldVm.PropertyChanged += OnFieldPropertyChanged;
        }

        UpdateTranslatedStatus();
    }

    public TranslatableEntry Entry => entry;

    public string ItemName => entry.ItemName;

    public string RelativePath => entry.RelativePath;

    public string FileType => entry.FileType;

    public ObservableCollection<TranslationFieldViewModel> FieldViewModels { get; } = new();

    public bool IsTranslated
    {
        get => isTranslated;
        private set => SetProperty(ref isTranslated, value);
    }

    private void OnFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranslationFieldViewModel.TranslatedValue))
        {
            // Sync back to the model
            if (sender is TranslationFieldViewModel fieldVm)
            {
                entry.TranslatedFields[fieldVm.FieldName] = fieldVm.TranslatedValue;
            }

            UpdateTranslatedStatus();
        }
    }

    private void UpdateTranslatedStatus()
    {
        IsTranslated = FieldViewModels.Any(f =>
            !string.IsNullOrWhiteSpace(f.TranslatedValue) &&
            !string.Equals(f.TranslatedValue, f.OriginalValue, StringComparison.Ordinal));
    }
}
