# 模型目录

所有模型均为 `Application/Models/` 下的简单 `{ get; set; }` 或 `{ get; init; }` POCO。全部为 `sealed`。

## 核心 / PAK

| 模型 | 关键字段 |
|-------|-----------|
| `PakManifest` | PakPath, CacheKey, Files, ModName, ModVersion, Author, WorkshopId |
| `PakLoadResult` | Manifest, LoadedFromCache, StatusMessage |
| `ResourceFileRecord` | RelativePath, FullPath, Extension, SizeBytes |
| `ModMetadata` | Name, FriendlyName, Author, Version, SteamContentId |

## 缓存

| 模型 | 关键字段 |
|-------|-----------|
| `CacheOverview` | TotalBytes, EntryCount, RecentEntries |
| `CacheEntrySummary` | CacheKey, PakPath, ModName, WorkshopId, CacheBytes, PakSize |

## 搜索与预览

| 模型 | 关键字段 |
|-------|-----------|
| `FilePreview` | Kind (Text/Image/Binary), SourceContent, ImageBytes, Content |
| `SearchHit` | FilePath, LineNumber, LineText, MatchStart, MatchLength |
| `DuplicateItemNameResult` | ItemName, Hits (FilePath + LineNumber) |
| `TextReadResult` | Content, Encoding, IsTruncated |
| `StarboundMarkup` | 颜色代码检测静态辅助类 |

## 补丁

| 模型 | 关键字段 |
|-------|-----------|
| `PatchSetManifest` | PatchKey, WorkshopId, ModName, SourcePakPath |
| `PatchSetSummary` | PatchKey, DisplayName, FileCount |
| `PatchFileRecord` | RelativePath, FullPath, SizeBytes |
| `PatchOverview` | Sets + TotalCount |

## 翻译

| 模型 | 关键字段 |
|-------|-----------|
| `TranslationProgressDocument` | ProjectKey, ProjectName, SourcePakPath, Files, OutputDirectory, ProviderSettings |
| `TranslationFileState` | RelativePath, SourceFingerprint, GenerationMode, Entries, IsSelected |
| `TranslationEntryState` | Path, Original, OriginalHash, Translated, Status, IsManuallyEdited |
| `TranslationEntryStatus` | 枚举: Pending=0, Translated=1, Verified=2, Skipped=3, Failed=4 |
| `TranslationGenerationMode` | 枚举: Auto=0, FileOverwrite=1, Patch=2 |
| `TranslationProviderSettings` | PreferredEngine, OpenAi, Google |
| `TranslationModMetadata` | Version, Author, ModName, FriendlyName, Description, Link, Priority |
| `TranslatableEntry` | RelativePath, ItemName, FileType, SourceFields, TranslatedFields |
| `TranslationSourceEntry` | Path, Original, TokenStartIndex, TokenEndIndex |
| `TranslationGlossaryEntry` | Source, Target |
| `TranslationEngineType` | 枚举: Google=0, OpenAI=1 |
| `GoogleTranslationSettings` | ProjectId, Location, ServiceAccountJsonPath, GlossaryName |
| `OpenAiTranslationSettings` | ApiKey, Model, BaseUrl |
| `TranslationFileAnalysis` | 单个文件的分析结果 |

## 设置

| 模型 | 关键字段 |
|-------|-----------|
| `AppSettings` | AssetUnpackerPath, AssetPackerPath, PakParentDirectory, PatchRootDirectory, CacheRootDirectory, TranslationRootDirectory |
