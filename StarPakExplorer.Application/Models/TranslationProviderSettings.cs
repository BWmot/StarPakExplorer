namespace StarPakExplorer.Application.Models;

public sealed class TranslationProviderSettings
{
    public TranslationEngineType PreferredEngine { get; set; } = TranslationEngineType.OpenAI;

    /// <summary>
    /// Target language (BCP-47 code) the translation engines should produce,
    /// e.g. "zh-CN", "zh-TW", "ja", "ko". The engines receive this via
    /// <see cref="TranslationProviderSettings"/> and translate game text (English
    /// source) into this language. Persisted per project in translation_progress.json.
    /// </summary>
    public string TargetLanguage { get; set; } = "zh-CN";

    public OpenAiTranslationSettings OpenAi { get; set; } = new();

    public GoogleTranslationSettings Google { get; set; } = new();
}
