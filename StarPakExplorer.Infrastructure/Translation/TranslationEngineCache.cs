using System.Text.Json;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Infrastructure.Translation;

/// <summary>
/// 翻译引擎配置缓存，保存在软件根目录下的 translation_engine_cache.json。
/// 解决每次开新项目都要重新填写引擎配置的问题。
/// </summary>
public sealed class TranslationEngineCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string cacheFilePath;

    public TranslationEngineCache()
    {
        var root = AppContext.BaseDirectory;
        cacheFilePath = Path.Combine(root, "translation_engine_cache.json");
    }

    public TranslationProviderSettings Load()
    {
        try
        {
            if (!File.Exists(cacheFilePath))
            {
                return new TranslationProviderSettings();
            }

            var json = File.ReadAllText(cacheFilePath);
            var cached = JsonSerializer.Deserialize<TranslationProviderSettings>(json, JsonOptions);
            return cached ?? new TranslationProviderSettings();
        }
        catch
        {
            return new TranslationProviderSettings();
        }
    }

    public void Save(TranslationProviderSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(cacheFilePath, json);
        }
        catch
        {
            // 缓存保存失败不影响翻译功能
        }
    }
}
