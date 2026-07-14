namespace StarPakExplorer.Application.Models;

public sealed class GoogleTranslationSettings
{
    public string ProjectId { get; set; } = "";
    public string Location { get; set; } = "global";
    public string ServiceAccountJsonPath { get; set; } = "";
    public string GlossaryName { get; set; } = "";
}
