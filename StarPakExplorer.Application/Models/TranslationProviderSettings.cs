namespace StarPakExplorer.Application.Models;

public sealed class TranslationProviderSettings
{
    public TranslationEngineType PreferredEngine { get; set; } = TranslationEngineType.OpenAI;

    public OpenAiTranslationSettings OpenAi { get; set; } = new();

    public GoogleTranslationSettings Google { get; set; } = new();
}
