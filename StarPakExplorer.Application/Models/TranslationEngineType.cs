namespace StarPakExplorer.Application.Models;

public enum TranslationEngineType
{
    /// <summary>Google Cloud Translation API v3（付费，需 Project ID + Service Account JSON）。</summary>
    Google = 0,

    /// <summary>OpenAI 兼容 chat/completions 接口。</summary>
    OpenAI = 1,

    /// <summary>谷歌翻译免费接口（非官方 translate.googleapis.com，无需任何配置）。</summary>
    GoogleFree = 2
}
