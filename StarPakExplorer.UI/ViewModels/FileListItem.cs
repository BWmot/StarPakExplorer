using StarPakExplorer.Application.Models;

namespace StarPakExplorer.UI.ViewModels;

public sealed class FileListItem
{
    public FileListItem(ResourceFileRecord file)
    {
        File = file;
        Category = GetCategory(file);
        Section = StarboundFileClassifier.Classify(file);
        Extension = file.Extension;
    }

    public ResourceFileRecord File { get; }

    public string DisplayPath => File.RelativePath;

    public FileCategory Category { get; }

    public StarboundFileSection Section { get; }

    public string Extension { get; }

    public string CategoryDisplayName => Category switch
    {
        FileCategory.Text => "文本",
        FileCategory.Image => "图片",
        FileCategory.Audio => "音频",
        FileCategory.Other => "其他",
        _ => "全部"
    };

    public string SectionDisplayName => Section switch
    {
        StarboundFileSection.Metadata => "元数据",
        StarboundFileSection.Items => "物品",
        StarboundFileSection.Objects => "对象",
        StarboundFileSection.NpcsAndMonsters => "NPC&怪物",
        StarboundFileSection.BiomesAndWorldgen => "世界生成",
        StarboundFileSection.Interface => "界面",
        StarboundFileSection.TexturesAndAnimation => "贴图动画",
        StarboundFileSection.Scripts => "脚本",
        StarboundFileSection.Audio => "音频",
        StarboundFileSection.Patch => "Patch",
        StarboundFileSection.Other => "其他",
        _ => "全部"
    };

    private static FileCategory GetCategory(ResourceFileRecord file)
    {
        var extension = file.Extension;

        if (IsImage(extension))
        {
            return FileCategory.Image;
        }

        if (IsAudio(extension))
        {
            return FileCategory.Audio;
        }

        if (IsText(extension))
        {
            return FileCategory.Text;
        }

        return FileCategory.Other;
    }

    private static bool IsText(string extension)
    {
        return extension is ".item"
            or ".activeitem"
            or ".object"
            or ".recipe"
            or ".patch"
            or ".config"
            or ".lua"
            or ".json"
            or ".metadata"
            or ".matitem"
            or ".material"
            or ".frames"
            or ".animation"
            or ".species"
            or ".codex"
            or ".questtemplate"
            or ".tech"
            or ".statuseffect"
            or ".projectile"
            or ".monsterpart"
            or ".monstertype"
            or ".npctype"
            or ".behavior"
            or ".biome"
            or ".surfacebiome"
            or ".undergroundbiome"
            or ".dungeon"
            or ".structure"
            or ".treasurepools";
    }

    private static bool IsImage(string extension)
    {
        return extension is ".png"
            or ".jpg"
            or ".jpeg"
            or ".gif"
            or ".bmp"
            or ".tif"
            or ".tiff"
            or ".webp";
    }

    private static bool IsAudio(string extension)
    {
        return extension is ".ogg"
            or ".wav"
            or ".mp3"
            or ".flac"
            or ".opus";
    }
}
