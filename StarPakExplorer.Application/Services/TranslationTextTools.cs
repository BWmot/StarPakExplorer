using System;
using System.Collections.Generic;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Services;

/// <summary>
/// Utility methods for translation text analysis and patch generation.
/// Currently a stub - actual implementation to be completed.
/// </summary>
internal static class TranslationTextTools
{
    public static bool IsTranslatableCandidate(string relativePath)
    {
        return false;
    }

    public static TranslationFileAnalysis AnalyzeFile(string fullPath, string relativePath)
    {
        throw new NotImplementedException();
    }

    public static TranslationGenerationMode DetermineSuggestedMode(string relativePath)
    {
        throw new NotImplementedException();
    }

    public static string BuildPatchFile(IEnumerable<TranslationEntryState> entries)
    {
        throw new NotImplementedException();
    }

    public static byte[] ApplyOverwriteTranslation(byte[] sourceBytes, IDictionary<string, string> translatedByPath)
    {
        throw new NotImplementedException();
    }

    public static string BuildMetadataJson(TranslationProgressDocument project)
    {
        throw new NotImplementedException();
    }

    public static string BuildReportHtml(TranslationProgressDocument project, object fileSummaries)
    {
        throw new NotImplementedException();
    }

    public static IDictionary<string, string> BuildTranslationCache(TranslationProgressDocument project)
    {
        throw new NotImplementedException();
    }

    public static IDictionary<string, IDictionary<string, string>> BuildFileTranslationCache(TranslationProgressDocument project)
    {
        throw new NotImplementedException();
    }

    public static Dictionary<string, string> BuildDefaultGlossary()
    {
        // Lightweight fallback glossary — primary source is the global glossary store.
        // These are common Starbound game terms that appear in most mod translations.
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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
        };
    }
}
