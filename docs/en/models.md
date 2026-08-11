# Model Catalog

All models are simple `{ get; set; }` or `{ get; init; }` POCOs under `Application/Models/`. All are `sealed`.

## Core / PAK

| Model | Key Fields |
|-------|-----------|
| `PakManifest` | PakPath, CacheKey, Files, ModName, ModVersion, Author, WorkshopId |
| `PakLoadResult` | Manifest, LoadedFromCache, StatusMessage |
| `ResourceFileRecord` | RelativePath, FullPath, Extension, SizeBytes |
| `ModMetadata` | Name, FriendlyName, Author, Version, SteamContentId |

## Cache

| Model | Key Fields |
|-------|-----------|
| `CacheOverview` | TotalBytes, EntryCount, RecentEntries |
| `CacheEntrySummary` | CacheKey, PakPath, ModName, WorkshopId, CacheBytes, PakSize |

## Search & Preview

| Model | Key Fields |
|-------|-----------|
| `FilePreview` | Kind (Text/Image/Binary), SourceContent, ImageBytes, Content |
| `SearchHit` | FilePath, LineNumber, LineText, MatchStart, MatchLength |
| `DuplicateItemNameResult` | ItemName, Hits (FilePath + LineNumber) |
| `TextReadResult` | Content, Encoding, IsTruncated |
| `StarboundMarkup` | Static helper for color code detection |

## Patches

| Model | Key Fields |
|-------|-----------|
| `PatchSetManifest` | PatchKey, WorkshopId, ModName, SourcePakPath |
| `PatchSetSummary` | PatchKey, DisplayName, FileCount |
| `PatchFileRecord` | RelativePath, FullPath, SizeBytes |
| `PatchOverview` | Sets + TotalCount |

## Translation

| Model | Key Fields |
|-------|-----------|
| `TranslationProgressDocument` | ProjectKey, ProjectName, SourcePakPath, Files, OutputDirectory, ProviderSettings |
| `TranslationFileState` | RelativePath, SourceFingerprint, GenerationMode, Entries, IsSelected |
| `TranslationEntryState` | Path, Original, OriginalHash, Translated, Status, IsManuallyEdited |
| `TranslationEntryStatus` | Enum: Pending=0, Translated=1, Verified=2, Skipped=3, Failed=4 |
| `TranslationGenerationMode` | Enum: Auto=0, FileOverwrite=1, Patch=2 |
| `TranslationProviderSettings` | PreferredEngine, OpenAi, Google |
| `TranslationModMetadata` | Version, Author, ModName, FriendlyName, Description, Link, Priority |
| `TranslatableEntry` | RelativePath, ItemName, FileType, SourceFields, TranslatedFields |
| `TranslationSourceEntry` | Path, Original, TokenStartIndex, TokenEndIndex |
| `TranslationGlossaryEntry` | Source, Target, Language (BCP-47, default zh-CN) |
| `TranslationEngineType` | Enum: Google=0, OpenAI=1 |
| `GoogleTranslationSettings` | ProjectId, Location, ServiceAccountJsonPath, GlossaryName |
| `OpenAiTranslationSettings` | ApiKey, Model, BaseUrl |
| `TranslationFileAnalysis` | Analysis result for a single file |

## Settings

| Model | Key Fields |
|-------|-----------|
| `AppSettings` | AssetUnpackerPath, AssetPackerPath, PakParentDirectory, PatchRootDirectory, CacheRootDirectory, TranslationRootDirectory |
