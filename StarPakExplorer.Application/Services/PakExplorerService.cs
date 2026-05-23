using System.Text.Json;
using System.Text.RegularExpressions;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Services;

public sealed partial class PakExplorerService
{
    private const int PreviewMaxBytes = 1_000_000;
    private const int ImagePreviewMaxBytes = 12_000_000;
    private const int SearchMaxBytesPerFile = 2_000_000;

    private static readonly HashSet<string> ItemNameExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".item",
        ".activeitem",
        ".object",
        ".matitem"
    };

    private readonly IAssetUnpacker unpacker;
    private readonly ICacheRepository cacheRepository;
    private readonly IMetadataReader metadataReader;
    private readonly IFileIndexService fileIndexService;
    private readonly ITextFileReader textFileReader;
    private readonly IAppLogger logger;

    public PakExplorerService(
        IAssetUnpacker unpacker,
        ICacheRepository cacheRepository,
        IMetadataReader metadataReader,
        IFileIndexService fileIndexService,
        ITextFileReader textFileReader,
        IAppLogger logger)
    {
        this.unpacker = unpacker;
        this.cacheRepository = cacheRepository;
        this.metadataReader = metadataReader;
        this.fileIndexService = fileIndexService;
        this.textFileReader = textFileReader;
        this.logger = logger;
    }

    public async Task<PakLoadResult> LoadPakAsync(
        string unpackerPath,
        string pakPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ValidateInput(unpackerPath, pakPath);

        var cacheKey = cacheRepository.GetCacheKey(pakPath);
        var manifest = await cacheRepository.TryLoadManifestAsync(cacheKey, cancellationToken);
        if (manifest is not null)
        {
            progress?.Report("缓存命中，正在加载文件列表...");
            logger.Info($"Loaded from cache: {pakPath}");
            return new PakLoadResult
            {
                Manifest = manifest,
                LoadedFromCache = true,
                StatusMessage = "已从缓存加载"
            };
        }

        progress?.Report("缓存未命中，正在解包...");
        await cacheRepository.PrepareFreshCacheAsync(cacheKey, cancellationToken);
        var unpackedDirectory = cacheRepository.GetUnpackedDirectory(cacheKey);

        await unpacker.UnpackAsync(unpackerPath, pakPath, unpackedDirectory, progress, cancellationToken);

        progress?.Report("正在读取 metadata...");
        var workshopId = WorkshopIdExtractor.Extract(pakPath);
        var metadata = await metadataReader.ReadAsync(unpackedDirectory, workshopId, cancellationToken);
        workshopId = FirstNonBlank(metadata.SteamContentId, workshopId);

        progress?.Report("正在建立文件索引...");
        var pakFile = new FileInfo(pakPath);
        manifest = new PakManifest
        {
            PakPath = pakPath,
            CacheKey = cacheKey,
            CreatedAt = DateTimeOffset.Now,
            PakSize = pakFile.Length,
            PakLastWriteTimeUtc = pakFile.LastWriteTimeUtc,
            WorkshopId = workshopId,
            ModName = metadata.GetDisplayName(workshopId, pakPath),
            Author = metadata.Author,
            Files = fileIndexService.BuildIndex(unpackedDirectory).ToList()
        };

        await cacheRepository.SaveManifestAsync(manifest, cancellationToken);
        logger.Info($"Unpacked and indexed: {pakPath}");

        return new PakLoadResult
        {
            Manifest = manifest,
            LoadedFromCache = false,
            StatusMessage = "已解包并写入缓存"
        };
    }

    public async Task<FilePreview> GetPreviewAsync(ResourceFileRecord file, CancellationToken cancellationToken)
    {
        if (textFileReader.IsImagePreviewSupported(file))
        {
            if (file.SizeBytes > ImagePreviewMaxBytes)
            {
                return new FilePreview
                {
                    Title = file.RelativePath,
                    Kind = PreviewKind.Unsupported,
                    Content = $"图片文件过大，暂不预览。\n\n路径: {file.RelativePath}\n大小: {FormatBytes(file.SizeBytes)}"
                };
            }

            var bytes = await textFileReader.ReadBinaryAsync(file.FullPath, ImagePreviewMaxBytes, cancellationToken);
            return new FilePreview
            {
                Title = file.RelativePath,
                Kind = PreviewKind.Image,
                Content = $"图片预览\n\n路径: {file.RelativePath}\n大小: {FormatBytes(file.SizeBytes)}",
                ImageBytes = bytes
            };
        }

        if (!textFileReader.IsPreviewSupported(file))
        {
            return new FilePreview
            {
                Title = file.RelativePath,
                Kind = PreviewKind.Unsupported,
                Content = $"不支持预览的文件类型\n\n路径: {file.RelativePath}\n大小: {FormatBytes(file.SizeBytes)}"
            };
        }

        var text = await textFileReader.ReadTextAsync(file.FullPath, PreviewMaxBytes, cancellationToken);
        var content = TryFormatJson(file, text.Content);
        if (text.WasTruncated)
        {
            content += "\n\n--- 文件较大，预览已截断 ---";
        }

        return new FilePreview
        {
            Title = file.RelativePath,
            Content = content,
            Kind = PreviewKind.Text,
            WasTruncated = text.WasTruncated
        };
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        PakManifest manifest,
        string keyword,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return [];
        }

        var hits = new List<SearchHit>();
        var searchableFiles = manifest.Files.Where(textFileReader.IsSearchSupported).ToList();
        for (var index = 0; index < searchableFiles.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = searchableFiles[index];
            if (index % 25 == 0)
            {
                progress?.Report($"搜索中 {index + 1}/{searchableFiles.Count}: {file.RelativePath}");
            }

            var text = await textFileReader.ReadTextAsync(file.FullPath, SearchMaxBytesPerFile, cancellationToken);
            AddSearchHits(hits, file.RelativePath, text.Content, keyword);
        }

        logger.Info($"Search completed. Keyword={keyword}, Hits={hits.Count}");
        return hits;
    }

    public async Task<IReadOnlyList<DuplicateItemNameResult>> ScanDuplicateItemNamesAsync(
        PakManifest manifest,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var candidates = manifest.Files
            .Where(file => ItemNameExtensions.Contains(file.Extension))
            .ToList();

        var grouped = new Dictionary<string, List<DuplicateItemNameHit>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = candidates[index];
            if (index % 20 == 0)
            {
                progress?.Report($"扫描 itemName {index + 1}/{candidates.Count}: {file.RelativePath}");
            }

            var text = await textFileReader.ReadTextAsync(file.FullPath, SearchMaxBytesPerFile, cancellationToken);
            AddItemNameHits(grouped, file.RelativePath, text.Content);
        }

        var duplicates = grouped
            .Where(pair => pair.Value.Count > 1)
            .Select(pair => new DuplicateItemNameResult
            {
                ItemName = pair.Key,
                Hits = pair.Value
            })
            .OrderByDescending(result => result.Count)
            .ThenBy(result => result.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.Info($"Duplicate itemName scan completed. Duplicates={duplicates.Count}");
        return duplicates;
    }

    public Task ClearCacheAsync(CancellationToken cancellationToken)
    {
        logger.Info("Clearing cache");
        return cacheRepository.ClearAsync(cancellationToken);
    }

    private static void ValidateInput(string unpackerPath, string pakPath)
    {
        if (!File.Exists(unpackerPath))
        {
            throw new FileNotFoundException("找不到 asset_unpacker.exe", unpackerPath);
        }

        if (!File.Exists(pakPath))
        {
            throw new FileNotFoundException("找不到 PAK 文件", pakPath);
        }
    }

    private static void AddSearchHits(List<SearchHit> hits, string relativePath, string content, string keyword)
    {
        var lines = SplitLines(content);
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add(new SearchHit
                {
                    FilePath = relativePath,
                    LineNumber = index + 1,
                    LineText = lines[index].Trim()
                });
            }
        }
    }

    private static void AddItemNameHits(
        Dictionary<string, List<DuplicateItemNameHit>> grouped,
        string relativePath,
        string content)
    {
        var lines = SplitLines(content);
        for (var index = 0; index < lines.Length; index++)
        {
            var match = ItemNameRegex().Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            var itemName = match.Groups["name"].Value;
            if (!grouped.TryGetValue(itemName, out var hits))
            {
                hits = [];
                grouped[itemName] = hits;
            }

            hits.Add(new DuplicateItemNameHit
            {
                FilePath = relativePath,
                LineNumber = index + 1
            });
        }
    }

    private static string TryFormatJson(ResourceFileRecord file, string content)
    {
        if (!LooksLikeJson(file))
        {
            return content;
        }

        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (JsonException)
        {
            return content;
        }
    }

    private static bool LooksLikeJson(ResourceFileRecord file)
    {
        if (string.Equals(Path.GetFileName(file.RelativePath), "_metadata", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.Equals(file.Extension, ".lua", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:0.##} KB";
        }

        return $"{bytes / 1024d / 1024d:0.##} MB";
    }

    private static string[] SplitLines(string content)
    {
        return content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    private static string? FirstNonBlank(string? first, string? second)
    {
        return !string.IsNullOrWhiteSpace(first) ? first : second;
    }

    [GeneratedRegex("\"itemName\"\\s*:\\s*\"(?<name>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ItemNameRegex();
}
