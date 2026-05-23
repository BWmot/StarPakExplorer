using StarPakExplorer.Application.Models;

namespace StarPakExplorer.UI.ViewModels;

public static class StarboundFileClassifier
{
    private static readonly string[] ExtensionPriority =
    [
        "",
        ".metadata",
        ".modinfo",
        ".patch",
        ".json",
        ".config",
        ".lua",
        ".png",
        ".frames",
        ".animation",
        ".item",
        ".activeitem",
        ".object",
        ".recipe",
        ".matitem",
        ".material",
        ".biome",
        ".surfacebiome",
        ".undergroundbiome",
        ".spacebiome",
        ".npctype",
        ".monstertype",
        ".monsterpart",
        ".monsterskill",
        ".treasurepools",
        ".treasurechests",
        ".tech",
        ".statuseffect",
        ".projectile",
        ".questtemplate",
        ".codex",
        ".species",
        ".ogg",
        ".wav",
        ".mp3",
        ".flac",
        ".opus"
    ];

    public static StarboundFileSection Classify(ResourceFileRecord file)
    {
        var path = file.RelativePath.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        var extension = file.Extension.ToLowerInvariant();

        if (IsMetadata(path, extension))
        {
            return StarboundFileSection.Metadata;
        }

        if (extension == ".patch")
        {
            return StarboundFileSection.Patch;
        }

        if (path.Contains("/scripts/") || extension == ".lua")
        {
            return StarboundFileSection.Scripts;
        }

        if (IsAudio(path, extension))
        {
            return StarboundFileSection.Audio;
        }

        if (IsItems(path, extension))
        {
            return StarboundFileSection.Items;
        }

        if (IsObjects(path, extension))
        {
            return StarboundFileSection.Objects;
        }

        if (IsNpcsAndMonsters(path, extension))
        {
            return StarboundFileSection.NpcsAndMonsters;
        }

        if (IsBiomesAndWorldgen(path, extension))
        {
            return StarboundFileSection.BiomesAndWorldgen;
        }

        if (path.Contains("/interface/") || path.Contains("/windowconfig/") || extension == ".pane")
        {
            return StarboundFileSection.Interface;
        }

        if (IsTexturesAndAnimation(path, extension))
        {
            return StarboundFileSection.TexturesAndAnimation;
        }

        return StarboundFileSection.Other;
    }

    public static int GetExtensionPriority(string extension)
    {
        var index = Array.FindIndex(ExtensionPriority, item => string.Equals(item, extension, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : int.MaxValue;
    }

    public static string GetExtensionDisplayName(string extension)
    {
        return string.IsNullOrWhiteSpace(extension) ? "(无后缀)" : extension;
    }

    private static bool IsMetadata(string path, string extension)
    {
        return path.EndsWith("/_metadata", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/.metadata", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/.modinfo", StringComparison.OrdinalIgnoreCase)
            || extension is ".metadata" or ".modinfo";
    }

    private static bool IsItems(string path, string extension)
    {
        return path.Contains("/items/")
            || extension is ".item" or ".activeitem" or ".matitem" or ".recipe";
    }

    private static bool IsObjects(string path, string extension)
    {
        return path.Contains("/objects/")
            || extension == ".object";
    }

    private static bool IsNpcsAndMonsters(string path, string extension)
    {
        return path.Contains("/npcs/")
            || path.Contains("/monsters/")
            || path.Contains("/behaviors/")
            || extension is ".npctype" or ".monstertype" or ".monsterpart" or ".monsterskill";
    }

    private static bool IsBiomesAndWorldgen(string path, string extension)
    {
        return path.Contains("/biomes/")
            || path.Contains("/celestial/")
            || path.Contains("/dungeons/")
            || path.Contains("/plants/")
            || path.Contains("/terrain/")
            || path.Contains("/weather/")
            || path.Contains("/worlds/")
            || path.Contains("/materials/")
            || extension is ".biome"
                or ".surfacebiome"
                or ".undergroundbiome"
                or ".spacebiome"
                or ".treasurepools"
                or ".treasurechests"
                or ".matmod"
                or ".damage";
    }

    private static bool IsTexturesAndAnimation(string path, string extension)
    {
        return path.Contains("/animation/")
            || path.Contains("/animations/")
            || extension is ".png"
                or ".jpg"
                or ".jpeg"
                or ".gif"
                or ".bmp"
                or ".tif"
                or ".tiff"
                or ".webp"
                or ".frames"
                or ".animation";
    }

    private static bool IsAudio(string path, string extension)
    {
        return path.Contains("/music/")
            || path.Contains("/sfx/")
            || path.Contains("/sound/")
            || extension is ".ogg" or ".wav" or ".mp3" or ".flac" or ".opus";
    }
}
