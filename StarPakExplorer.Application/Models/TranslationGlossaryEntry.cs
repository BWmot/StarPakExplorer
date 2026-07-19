namespace StarPakExplorer.Application.Models;

public sealed class TranslationGlossaryEntry
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";

    /// <summary>Source of this entry: User, Imported, or AutoFromCache.</summary>
    public GlossaryEntrySource EntrySource { get; set; } = GlossaryEntrySource.Imported;

    /// <summary>When this entry was last modified.</summary>
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>Optional category tag (e.g. "Ore", "NPC", "Item").</summary>
    public string? Category { get; set; }

    /// <summary>Free-form notes.</summary>
    public string? Notes { get; set; }
}

public enum GlossaryEntrySource
{
    /// <summary>Imported from an external term bank file.</summary>
    Imported = 0,

    /// <summary>Manually added by the user.</summary>
    User = 1,

    /// <summary>Auto-saved from a completed translation.</summary>
    AutoFromCache = 2
}
