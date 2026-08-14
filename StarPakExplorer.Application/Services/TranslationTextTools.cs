using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Services;

/// <summary>
/// Utility methods for translation text analysis and patch generation.
/// 该类型同时被精修工具(TranslationSourceReader)与翻译管理器(TranslationService)复用，
/// 保证两处对“可翻译内容”的判定与提取完全一致。
/// </summary>
public static class TranslationTextTools
{
    /// <summary>
    /// 可参与翻译提取的 JSON 文件扩展名白名单。
    /// 在原有 item/activeitem/object/matitem/codex 基础上，补充了 dialog(.config/.converse)、
    /// npc(.npctype)、quest(.questtemplate)、interface(.config)、species(.species) 等类别，
    /// 使扫描工具不再漏掉这 6 类内容。
    /// </summary>
    public static readonly HashSet<string> TranslatableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".item",
        ".activeitem",
        ".object",
        ".matitem",
        ".codex",
        ".config",
        ".npctype",
        ".questtemplate",
        ".converse",
        ".species"
    };

    /// <summary>
    /// 物品类文件只提取顶层这几个知名描述字段，避免把 item/object 里的脚本、贴图等技术负载暴露成翻译项。
    /// </summary>
    private static readonly HashSet<string> ItemTranslatableFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "shortdescription",
        "description",
        "apexDescription",
        "avianDescription",
        "floranDescription",
        "glitchDescription",
        "humanDescription",
        "hylotlDescription",
        "novakidDescription",
        "feneroxDescription"
    };

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions PatchJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        // Starbound 的 .patch 要求键全部小写：op / path / value。
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>以扩展名结尾的字符串，表示是资源路径/引用而非用户可见文本。</summary>
    private static readonly HashSet<string> AssetExtensionSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lua", ".png", ".json", ".config", ".frames", ".wav", ".ogg", ".obj", ".tile",
        ".patch", ".animation", ".song", ".dll", ".txt", ".log", ".preview", ".image",
        ".species", ".npctype", ".questtemplate", ".item", ".activeitem", ".object",
        ".matitem", ".codex", ".particles", ".treasure", ".liquid", ".liqitem", ".consumable"
    };

    /// <summary>结构性的单字关键词（UI 控件类型、状态、对齐、布尔等），绝不翻译。</summary>
    private static readonly HashSet<string> StructuralKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "none", "null", "default", "auto", "left", "right", "top", "bottom",
        "center", "middle", "full", "visible", "hidden", "enabled", "disabled", "label",
        "button", "textbox", "image", "scrollarea", "checkbox", "radio", "dropdown",
        "slider", "progress", "list", "table", "stack", "grid", "layer", "pane", "window",
        "panel", "text", "value", "title", "caption", "subtitle", "header", "footer",
        "horizontal", "vertical", "fill", "wrap", "anchor", "offset", "padding", "margin",
        "on", "off", "yes", "no", "ok", "cancel", "close", "open", "confirm", "accept",
        "idle", "walk", "run", "jump", "fall", "sit", "sleep", "swim", "dance", "celebrate",
        "aim", "attack", "hit", "hurt", "dead", "death", "birth", "render", "frame", "frames"
    };

    public static bool IsTranslatableCandidate(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var extension = Path.GetExtension(relativePath);
        return TranslatableExtensions.Contains(extension);
    }

    public static TranslationFileAnalysis AnalyzeFile(string fullPath, string relativePath)
    {
        var fields = ExtractTranslatableFieldsFromFile(fullPath, relativePath);

        var entries = fields
            .Select(pair => new TranslationSourceEntry
            {
                Path = pair.Key,
                Original = pair.Value
            })
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();

        return new TranslationFileAnalysis
        {
            RelativePath = relativePath,
            SourceFingerprint = ComputeFingerprint(fields),
            SuggestedMode = TranslationGenerationMode.Patch,
            Entries = entries
        };
    }

    public static TranslationGenerationMode DetermineSuggestedMode(string relativePath)
    {
        // 对 Starbound Mod 汉化，.patch 是标准且最安全的输出方式。
        _ = relativePath;
        return TranslationGenerationMode.Patch;
    }

    public static string BuildPatchFile(IEnumerable<TranslationEntryState> entries)
    {
        var operations = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Translated))
            .Where(entry => !string.Equals(entry.Translated, entry.Original, StringComparison.Ordinal))
            .Select(entry => new JsonPatchOperation
            {
                Op = "replace",
                Path = NormalizePatchPath(entry.Path),
                Value = entry.Translated!
            })
            .ToList();

        return JsonSerializer.Serialize(operations, PatchJsonOptions);
    }

    public static byte[] ApplyOverwriteTranslation(byte[] sourceBytes, IDictionary<string, string> translatedByPath)
    {
        // 管理器默认建议 Patch 模式；仅当用户手动切到 FileOverwrite 时才走这里。
        try
        {
            var jsonText = Encoding.UTF8.GetString(sourceBytes);
            using var doc = JsonDocument.Parse(SanitizeJsonForParse(jsonText), JsonOptions);

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Indented = false
            }))
            {
                WriteWithOverrides(doc.RootElement, "", translatedByPath, writer);
            }

            return stream.ToArray();
        }
        catch (Exception)
        {
            // 任何解析/重写失败都回退到原始文件，绝不破坏源文件。
            return sourceBytes;
        }
    }

    public static string BuildMetadataJson(TranslationProgressDocument project)
    {
        var metadataObj = new Dictionary<string, object>
        {
            ["version"] = "2.1.1",
            ["author"] = project.SourceAuthor ?? string.Empty,
            ["name"] = project.ProjectName,
            ["description"] = $"{project.SourceModName} 中文汉化补丁",
            ["friendlyName"] = project.ProjectName,
            ["link"] = string.Empty,
            ["priority"] = -68,
            ["requires"] = new[] { project.SourceModName }
        };

        return JsonSerializer.Serialize(metadataObj, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        });
    }

    public static string BuildReportHtml(TranslationProgressDocument project, object fileSummaries)
    {
        _ = fileSummaries;

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html><head><meta charset=\"utf-8\">");
        builder.AppendLine($"<title>{HtmlEncode(project.ProjectName)} 翻译报告</title>");
        builder.AppendLine("</head><body>");
        builder.AppendLine($"<h1>{HtmlEncode(project.ProjectName)}</h1>");
        builder.AppendLine($"<p>共 {project.Files.Count} 个文件</p>");
        builder.AppendLine("<table border=\"1\" cellspacing=\"0\" cellpadding=\"4\" style=\"border-collapse:collapse\">");
        builder.AppendLine("<tr><th>文件</th><th>已翻译</th><th>待翻译</th><th>备注</th></tr>");

        foreach (var file in project.Files)
        {
            var translated = file.Entries.Count(entry => !string.IsNullOrWhiteSpace(entry.Translated));
            var pending = file.Entries.Count - translated;
            builder.AppendLine(
                $"<tr><td>{HtmlEncode(file.RelativePath)}</td>" +
                $"<td>{translated}</td><td>{pending}</td>" +
                $"<td>{HtmlEncode(file.LastError ?? string.Empty)}</td></tr>");
        }

        builder.AppendLine("</table></body></html>");
        return builder.ToString();
    }

    /// <summary>
    /// 翻译缓存键：目标语言 + 原文。同一原文翻译成不同语言时不互相污染缓存。
    /// </summary>
    public static string BuildCacheKey(string targetLanguage, string sourceText)
    {
        var language = string.IsNullOrWhiteSpace(targetLanguage) ? "zh-CN" : targetLanguage.Trim();
        return $"{language}\u001E{sourceText}";
    }

    public static IDictionary<string, string> BuildTranslationCache(TranslationProgressDocument project)
    {
        var targetLanguage = project.ProviderSettings.TargetLanguage;
        return project.Files
            .SelectMany(file => file.Entries)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Translated))
            .GroupBy(entry => entry.Original, StringComparer.Ordinal)
            .ToDictionary(
                group => BuildCacheKey(targetLanguage, group.Key),
                group => group.First().Translated!,
                StringComparer.Ordinal);
    }

    public static IDictionary<string, IDictionary<string, string>> BuildFileTranslationCache(TranslationProgressDocument project)
    {
        var cache = new Dictionary<string, IDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in project.Files)
        {
            var fileTranslations = file.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Translated))
                .ToDictionary(
                    entry => entry.Path,
                    entry => entry.Translated!,
                    StringComparer.OrdinalIgnoreCase);

            if (fileTranslations.Count > 0)
            {
                cache[file.RelativePath] = fileTranslations;
            }
        }

        return cache;
    }

    public static TranslationGlossary BuildDefaultGlossary()
    {
        // Lightweight fallback glossary — primary source is the global glossary store.
        // These are common Starbound game terms that appear in most mod translations.
        return new TranslationGlossary
        {
            Lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Copper Ore", "铜矿石" },
                { "Copper Bar", "铜锭" },
                { "Iron Ore", "铁矿石" },
                { "Iron Bar", "铁锭" },
                { "Silver Ore", "银矿石" },
                { "Silver Bar", "银锭" },
                { "Gold Ore", "金矿石" },
                { "Gold Bar", "金锭" },
                { "Tungsten Ore", "钨矿石" },
                { "Tungsten Bar", "钨锭" },
                { "Titanium Ore", "钛矿石" },
                { "Titanium Bar", "钛锭" },
                { "Durasteel Ore", "耐钢矿" },
                { "Durasteel Bar", "耐钢锭" },
                { "Aegisalt Ore", "霓磷盐矿" },
                { "Aegisalt Bar", "霓磷盐锭" },
                { "Ferozium Ore", "菲洛合金矿" },
                { "Ferozium Bar", "菲洛合金锭" },
                { "Violium Ore", "维奥合金矿" },
                { "Violium Bar", "维奥合金锭" },
                { "Solarium Ore", "日耀矿" },
                { "Solarium Bar", "日耀锭" },
                { "Core Fragment", "核心碎片" },
                { "Diamond", "钻石" },
                { "Coal", "煤炭" },
                { "Pixel", "像素" },
                { "Matter Manipulator", "物质操纵器" },
                { "Protectorate", "守护团" },
                { "Terramart", "大地集市" },
                { "Frogg Furnishing", "蛙蛙家具" },
                { "Penguin Bay", "企鹅湾" },
                { "Outpost", "前哨站" },
                { "Ark", "方舟" },
                { "Erchius", "厄尔吉斯" },
                { "Floran", "叶族" },
                { "Hylotl", "鲛人" },
                { "Avian", "翼族" },
                { "Apex", "猿族" },
                { "Glitch", "机械族" },
                { "Novakid", "新星族" },
                { "Human", "人类" },
            },
            AmbiguousTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Apex", "Glitch", "Avian", "Human", "Ark"
            }
        };
    }

    // ==================== 共享提取逻辑 ====================

    /// <summary>
    /// 从 JSON 文本中提取所有 (路径 → 原文) 可翻译字段。
    /// 物品类文件只提取顶层知名描述字段；结构化文件（dialog/config/npctype/questtemplate/
    /// converse/species/codex）做深度遍历，收集所有用户可见的字符串叶子。
    /// 返回的 key 为不带前导斜杠的 JSON 指针路径（例如 "greeting/default/default/default/0"）。
    /// </summary>
    public static Dictionary<string, string> ExtractTranslatableFields(string jsonText, string fileType)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Starbound 允许 JSON 字符串内出现字面换行/制表符，需先转义再交给严格解析器。
        using var doc = JsonDocument.Parse(SanitizeJsonForParse(jsonText), JsonOptions);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        if (IsItemLike(fileType))
        {
            foreach (var fieldName in ItemTranslatableFields)
            {
                if (root.TryGetProperty(fieldName, out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    var value = prop.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result[fieldName] = value;
                    }
                }
            }

            return result;
        }

        CollectStrings(root, "", result);
        return result;
    }

    /// <summary>提取 itemName / objectName，用于展示条目名；失败则用文件名兜底。</summary>
    public static string GetItemName(string jsonText, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(SanitizeJsonForParse(jsonText), JsonOptions);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return fallback;
            }

            return GetStringProperty(root, "itemName")
                ?? GetStringProperty(root, "objectName")
                ?? fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static Dictionary<string, string> ExtractTranslatableFieldsFromFile(string fullPath, string relativePath)
    {
        var jsonText = File.ReadAllText(fullPath);
        var fileType = Path.GetExtension(relativePath);
        return ExtractTranslatableFields(jsonText, fileType);
    }

    private static bool IsItemLike(string? fileType)
    {
        return fileType is null
            || fileType.Equals(".item", StringComparison.OrdinalIgnoreCase)
            || fileType.Equals(".activeitem", StringComparison.OrdinalIgnoreCase)
            || fileType.Equals(".object", StringComparison.OrdinalIgnoreCase)
            || fileType.Equals(".matitem", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Starbound 的 JSON 允许字符串内出现字面换行/制表符等控制字符，而 .NET 的严格解析器不允许。
    /// 在解析前把字符串内部的裸控制字符转义为 \n \r \t \uXXXX，既保留原文语义又让解析通过。
    /// 只在双引号字符串内部处理，注释、数组/对象结构均不受影响。
    /// </summary>
    internal static string SanitizeJsonForParse(string text)
    {
        var builder = new StringBuilder(text.Length + 16);
        var inString = false;
        var escaped = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inString)
            {
                if (escaped)
                {
                    builder.Append(ch);
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    builder.Append(ch);
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    builder.Append(ch);
                    inString = false;
                    continue;
                }

                if (ch == '\n')
                {
                    builder.Append("\\n");
                    continue;
                }

                if (ch == '\r')
                {
                    builder.Append("\\r");
                    continue;
                }

                if (ch == '\t')
                {
                    builder.Append("\\t");
                    continue;
                }

                if (ch < 0x20)
                {
                    builder.Append("\\u").Append(((int)ch).ToString("x4"));
                    continue;
                }

                builder.Append(ch);
            }
            else
            {
                if (ch == '"')
                {
                    inString = true;
                }

                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static void CollectStrings(JsonElement element, string pointer, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectStrings(property.Value, JoinPointer(pointer, property.Name), result);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    CollectStrings(item, $"{pointer}/{index}", result);
                    index++;
                }
                break;

            case JsonValueKind.String:
                var text = element.GetString();
                if (IsTranslatableText(text) && !string.IsNullOrEmpty(pointer))
                {
                    result[pointer.Substring(1)] = text!;
                }
                break;
        }
    }

    private static string JoinPointer(string pointer, string name)
    {
        var escaped = name.Replace("~", "~0").Replace("/", "~1");
        return string.IsNullOrEmpty(pointer) ? $"/{escaped}" : $"{pointer}/{escaped}";
    }

    private static bool IsTranslatableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();

        if (text.Length < 2 || text.Length > 2000)
        {
            return false;
        }

        // 必须包含至少一个字母。
        if (!text.Any(char.IsLetter))
        {
            return false;
        }

        // 跳过形如 "d35eae" 的十六进制颜色值。
        if (text.Length <= 8 && text.All(ch => Uri.IsHexDigit(ch)))
        {
            return false;
        }

        // 跳过资源路径 / 文件引用（例如 "scripts/foo.lua"、"foo.png"）。
        if (text.Contains('\\') || HasAssetExtension(text))
        {
            return false;
        }

        // 跳过无空格的裸路径 a/b/c。
        if (text.Contains('/') && !text.Any(char.IsWhiteSpace))
        {
            return false;
        }

        // 跳过无空格的超长 token（id/哈希）。
        if (text.Length > 60 && !text.Any(char.IsWhiteSpace))
        {
            return false;
        }

        // 跳过结构性的单字关键词（控件类型、状态、对齐、布尔等）。
        if (!text.Any(char.IsWhiteSpace) && text.Length <= 32 && StructuralKeywords.Contains(text))
        {
            return false;
        }

        // 跳过纯数字/数值表达式。
        if (text.All(ch => char.IsDigit(ch) || ch == '.' || ch == ',' || ch == '-' || ch == '+' || ch == '%'))
        {
            return false;
        }

        return true;
    }

    private static bool HasAssetExtension(string text)
    {
        var dot = text.LastIndexOf('.');
        if (dot < 0)
        {
            return false;
        }

        var suffix = text.Substring(dot);
        return AssetExtensionSuffixes.Contains(suffix);
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }

        return null;
    }

    private static string NormalizePatchPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        return path.StartsWith("/", StringComparison.Ordinal) ? path : $"/{path}";
    }

    private static string ComputeFingerprint(Dictionary<string, string> fields)
    {
        var joined = string.Join(
            "\u001f",
            fields.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                  .Select(pair => $"{pair.Key}\u001e{pair.Value}"));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined))).ToLowerInvariant();
    }

    private static string HtmlEncode(string value)
    {
        return System.Net.WebUtility.HtmlEncode(value);
    }

    private static void WriteWithOverrides(
        JsonElement element,
        string pointer,
        IDictionary<string, string> translatedByPath,
        Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteWithOverrides(property.Value, JoinPointer(pointer, property.Name), translatedByPath, writer);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    WriteWithOverrides(item, $"{pointer}/{index}", translatedByPath, writer);
                    index++;
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                var key = pointer.Length > 0 ? pointer.Substring(1) : "";
                if (translatedByPath.TryGetValue(key, out var replacement) && !string.IsNullOrWhiteSpace(replacement))
                {
                    writer.WriteStringValue(replacement);
                }
                else
                {
                    writer.WriteStringValue(element.GetString());
                }
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private sealed class JsonPatchOperation
    {
        public string Op { get; init; } = "replace";
        public string Path { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
    }
}
