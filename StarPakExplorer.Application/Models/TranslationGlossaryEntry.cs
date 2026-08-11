namespace StarPakExplorer.Application.Models;

public sealed class TranslationGlossaryEntry
{
    /// <summary>The original (source-language) term, e.g. "Copper Ore".</summary>
    public string Source { get; set; } = "";

    /// <summary>The translated term in <see cref="Language"/>, e.g. "铜矿石".</summary>
    public string Target { get; set; } = "";

    /// <summary>BCP-47 language code of the target, e.g. "zh-CN", "zh-TW", "ja", "ko", "en".</summary>
    public string Language { get; set; } = "zh-CN";

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
