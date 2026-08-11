using StarPakExplorer.Application.Models;

namespace StarPakExplorer.UI.ViewModels;

/// <summary>
/// An editable row shown in the glossary window. Wraps a
/// <see cref="TranslationGlossaryEntry"/> and tracks the originally persisted
/// Source + Language so that renaming a term can update the primary key
/// <c>(Source, Language)</c> correctly.
/// </summary>
public sealed class GlossaryEntryRow : ViewModelBase
{
    private string source;
    private string language;
    private string target;
    private GlossaryEntrySource entrySource;
    private string? category;
    private string? notes;
    private DateTimeOffset modifiedAt;

    public GlossaryEntryRow(TranslationGlossaryEntry entry)
    {
        source = entry.Source;
        language = string.IsNullOrWhiteSpace(entry.Language) ? "zh-CN" : entry.Language.Trim();
        target = entry.Target;
        entrySource = entry.EntrySource;
        category = entry.Category;
        notes = entry.Notes;
        modifiedAt = entry.ModifiedAt;
        OriginalSource = entry.Source;
        OriginalLanguage = language;
    }

    /// <summary>The Source value at the time the row was loaded (part of the DB primary key).</summary>
    public string OriginalSource { get; private set; }

    /// <summary>The Language value at the time the row was loaded (part of the DB primary key).</summary>
    public string OriginalLanguage { get; private set; }

    /// <summary>True while this row is a brand-new term that has not been persisted yet.</summary>
    public bool IsNew { get; set; }

    public string Source
    {
        get => source;
        set => SetProperty(ref source, value);
    }

    public string Language
    {
        get => language;
        set => SetProperty(ref language, value);
    }

    public string Target
    {
        get => target;
        set => SetProperty(ref target, value);
    }

    public GlossaryEntrySource EntrySource
    {
        get => entrySource;
        set => SetProperty(ref entrySource, value);
    }

    public string? Category
    {
        get => category;
        set => SetProperty(ref category, value);
    }

    public string? Notes
    {
        get => notes;
        set => SetProperty(ref notes, value);
    }

    public DateTimeOffset ModifiedAt
    {
        get => modifiedAt;
        set
        {
            if (SetProperty(ref modifiedAt, value))
            {
                OnPropertyChanged(nameof(ModifiedAtText));
            }
        }
    }

    public string ModifiedAtText => ModifiedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public string EntrySourceLabel => EntrySource switch
    {
        GlossaryEntrySource.User => "用户",
        GlossaryEntrySource.AutoFromCache => "自动",
        _ => "导入"
    };

    /// <summary>Marks this row as successfully persisted, updating the tracked key.</summary>
    public void MarkPersisted(string persistedSource, string persistedLanguage, DateTimeOffset at)
    {
        OriginalSource = persistedSource;
        OriginalLanguage = persistedLanguage;
        IsNew = false;
        ModifiedAt = at;
    }
}
