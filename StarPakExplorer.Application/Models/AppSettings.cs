namespace StarPakExplorer.Application.Models;

public sealed class AppSettings
{
    public string AssetUnpackerPath { get; set; } = "";
    public string AssetPackerPath { get; set; } = "";
    public string PakParentDirectory { get; set; } = "";
    public string PatchRootDirectory { get; set; } = "";
    public string CacheRootDirectory { get; set; } = "";
    public string TranslationRootDirectory { get; set; } = "";

    /// <summary>Path to the global glossary database file (SQLite). Leave empty to use the default next to the executable.</summary>
    public string GlobalGlossaryPath { get; set; } = "";

    /// <summary>
    /// Configured target languages for the global glossary (BCP-47 codes).
    /// The first entry is the default language for new entries. Users can add
    /// more languages here (e.g. "zh-TW", "ja", "ko", "en") to translate terms
    /// into languages other than Simplified Chinese.
    /// </summary>
    public List<string> GlossaryLanguages { get; set; } = new() { "zh-CN" };
}
