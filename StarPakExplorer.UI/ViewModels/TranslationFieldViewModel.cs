namespace StarPakExplorer.UI.ViewModels;

/// <summary>
/// 表示单个可翻译字段的 ViewModel（一行：字段名 | 原文 | 翻译）
/// </summary>
public sealed class TranslationFieldViewModel : ViewModelBase
{
    private string translatedValue;

    public TranslationFieldViewModel(string fieldName, string originalValue, string translatedValue)
    {
        FieldName = fieldName;
        OriginalValue = originalValue;
        this.translatedValue = translatedValue;
    }

    public string FieldName { get; }

    public string OriginalValue { get; }

    public string DisplayName => GetDisplayName(FieldName);

    public string TranslatedValue
    {
        get => translatedValue;
        set => SetProperty(ref translatedValue, value);
    }

    private static string GetDisplayName(string fieldName)
    {
        return fieldName switch
        {
            "shortdescription" => "简短名称",
            "description" => "详细描述",
            "apexDescription" => "猿族描述",
            "avianDescription" => "翼族描述",
            "floranDescription" => "叶族描述",
            "glitchDescription" => "机械族描述",
            "humanDescription" => "人类描述",
            "hylotlDescription" => "鲛族描述",
            "novakidDescription" => "星裔描述",
            "feneroxDescription" => "狐族描述",
            _ => fieldName
        };
    }
}
