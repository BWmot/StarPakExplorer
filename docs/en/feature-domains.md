# Feature Domains

## PAK Loading (Cache → Unpack → Index)

**Flow**: `PakExplorerService.LoadPakAsync()`:

1. Compute `cacheKey = SHA256(pakPath)[:32]`
2. Try `cacheRepository.TryLoadManifestAsync(cacheKey)` → cache hit returns instantly
3. Cache miss: `cacheRepository.PrepareFreshCacheAsync()` → `assetUnpacker.UnpackAsync()` (calls external `asset_unpacker.exe` via `Process.Start`)
4. `metadataReader.ReadAsync()` → parse `_metadata` or `.metadata` JSON
5. `fileIndexService.BuildIndex()` → `Directory.EnumerateFiles()` all unpacked files to `List<ResourceFileRecord>`
6. Save `PakManifest` JSON to cache, return `PakLoadResult`

**Key players**:

| Role | Interface → Implementation | Location |
|------|---------------------------|----------|
| Unpacking | `IAssetUnpacker` → `AssetUnpacker` | `Infrastructure/Unpacking/` |
| Cache | `ICacheRepository` → `CacheRepository` | `Infrastructure/Cache/` |
| Metadata | `IMetadataReader` → `MetadataReader` | `Infrastructure/Metadata/` |
| Indexing | `IFileIndexService` → `FileIndexService` | `Infrastructure/Indexing/` |

## Cache Management

- **Location**: `%LOCALAPPDATA%\StarPakExplorer\Cache\{sha256[:32]}\unpacked\`
- `CacheRepository` computes SHA256 of pakPath, uses first 32 hex chars as key
- Stores `manifest.json` in cache root; `unpacked/` subdirectory holds extracted files
- Main window shows cache overview with delete/clear capabilities

`ICacheRepository` methods: `GetCacheKey()`, `TryLoadManifestAsync()`, `PrepareFreshCacheAsync()`, `SaveManifestAsync()`, `GetUnpackedDirectory()`, `GetOverviewAsync()`, `DeleteAsync()`, `ClearAllAsync()`

## File Browsing & Search

### File Sections (11 tabs in MainWindow)

All → Metadata → Items → Objects → NPCs&Monsters → Biomes&Worldgen → Interface → Textures&Animation → Scripts → Audio → Patch → Other

Classification: `StarboundFileClassifier` maps file extensions to `FileCategory` and `StarboundFileSection`.

### Preview

`PakExplorerService.GetPreviewAsync()` → `FilePreview` (Text/Image/Binary).  
`ITextFileReader` handles text files up to 1MB, images up to 12MB.

### Search

`PakExplorerService.SearchAsync()` — keyword search within text file contents (up to 2MB per file), returns `List<SearchHit>`.

### Duplicate Scan

`PakExplorerService.ScanDuplicateItemNamesAsync()` — scans `.item`/`.activeitem`/`.object`/`.matitem` for duplicate `itemName` fields, returns `List<DuplicateItemNameResult>`.

## Patch Management

Patches are text/binary modifications applied on top of original mod files.

**Flow**:
1. Select file in MainWindow → double-click → `FileModifyWindow` opens
2. `FileModifyViewModel` loads file content, allows text editing (with encoding selection) or binary replacement
3. On save → `IPatchStore.SaveTextAsync()` or `SaveReplacementAsync()` → writes to `%LOCALAPPDATA%\StarPakExplorer\Patches\{patchKey}/`
4. `PatchManagerWindow` shows all patch sets, allows pack-to-.pak export

**Patch key**: `workshop_{id}` for Workshop mods, else `{name}_{hash}`.

**Key interface**: `IPatchStore` → `PatchStore` (`Infrastructure/Patches/`)  
Methods: `GetPatchRoot()`, `GetPatchKey()`, `EnsurePatchSetAsync()`, `SaveTextAsync()`, `SaveReplacementAsync()`, `GetPatchSetsAsync()`, `GetPatchFilesAsync()`, `DeleteAsync()`

**File Staging**: `IFileStagingStore` → `FileStagingStore` — staging area for modified files before they become patches.

## Pack Export (`PackManagerWindow`)

`PackManagerViewModel` → browse source directory → tree view (`PackTreeNodeViewModel`) → select files → `ExportCommand` → `IAssetPacker.PackAsync()` calls external `asset_packer.exe`.

## Settings

- **Location**: `%LOCALAPPDATA%\StarPakExplorer\settings.json`
- **Store**: `IAppSettingsStore` → `JsonAppSettingsStore` (JSON serialization)

**`AppSettings`** (7 string properties):

| Property | Purpose |
|----------|---------|
| `AssetUnpackerPath` | Path to Starbound's `asset_unpacker.exe` |
| `AssetPackerPath` | Path to Starbound's `asset_packer.exe` |
| `PakParentDirectory` | Default directory for `.pak` file selection |
| `PatchRootDirectory` | Custom patch storage root (default: `%LOCALAPPDATA%\StarPakExplorer\Patches`) |
| `CacheRootDirectory` | Custom cache root (default: `%LOCALAPPDATA%\StarPakExplorer\Cache`) |
| `TranslationRootDirectory` | Custom translation project root (default: `%LOCALAPPDATA%\StarPakExplorer\Translations`) |
| `GlobalGlossaryPath` | Global glossary SQLite database path (default blank: uses `<install directory>\global_glossary.db`) |

## Translation Pipeline & Glossary

The translation system supports two workflows: standalone translation (`TranslationWindow`) and the full project pipeline (`TranslationManagerWindow`). See [translation-pipeline.md](translation-pipeline.md) for details.

### Project Pipeline Overview

**Four-stage flow**: Create Project → Scan → Translate → Generate

1. **Create/Load**: `TranslationService.LoadOrCreateProjectAsync()` → `TranslationProgressDocument` persisted to `%LOCALAPPDATA%\StarPakExplorer\Translations\{projectKey}/`
2. **Scan**: `TranslationService.ScanAsync()` enumerates `.item`/`.activeitem`/`.object`/`.matitem`/`.codex` files, extracts `shortdescription`, `description`, and 8 race description fields
3. **Translate**: `TranslationService.TranslatePendingAsync()` batch-translates (30 entries per batch), supports Google Cloud Translation API v3 and OpenAI API engines
4. **Generate**: `TranslationService.GenerateOutputAsync()` outputs `.patch` files + `_metadata`

### Glossary System

The translation system uses a dual-layer glossary architecture to ensure translation consistency:

#### Layer 1: Project Glossary

Each translation project maintains an independent project-level glossary, stored as `glossary.json` in the project directory. Project glossary entries only take effect within that project, allowing different mods to use different term mappings.

#### Layer 2: Global Glossary

**Storage**: SQLite database at `<install directory>\global_glossary.db` (customizable path in settings). Stored via `SqliteGlobalGlossaryStore` using `Microsoft.Data.Sqlite`; the legacy `global_glossary.json` file is automatically migrated to the database on first launch (the old file is renamed to `global_glossary.json.migrated`).

**Interface**: `IGlobalGlossaryStore` → `SqliteGlobalGlossaryStore` (`Infrastructure/Translation/`)

Core methods:
- `LoadAllAsync()` / `SaveAllAsync()` — Load/save the global glossary
- `UpsertAsync(entry)` / `UpsertManyAsync(entries)` — Add or update entries (batch upsert keeps the table small and fast)
- `DeleteAsync(source, language)` / `DeleteManyAsync(keys)` — Remove entries
- `SearchAsync(keyword, language, limit)` — LIKE-based search across source/target/category/notes (case-insensitive, up to 2000 rows in the UI)
- `CountAsync()` — Total entry count
- `ImportFromFileAsync(path, language)` — Import from external term bank file (supports `English|||Chinese` format, keeps existing entries)
- `ExportToFileAsync(path)` — Export glossary to file
- `BuildLookupAsync(language)` — Build a `Dictionary<string, string>` lookup table

#### Glossary Merge Strategy

`TranslationService.EnsureGlossaryAsync()` merges glossaries in the following priority order before each translation:

1. **Project Glossary** — Highest priority
2. **Global Glossary** — Fallback supplement
3. **Built-in Default Glossary** — `TranslationTextTools.BuildDefaultGlossary()` provides ~40 common Starbound terms (ores: Copper/铜, Iron/铁, Gold/金, Titanium/钛, etc.; races: Floran/叶族, Hylotl/鲛人, Avian/翼族, etc.)

#### Post-Translation Sync

After each translation completes, `TranslationService.SyncToGlobalGlossaryAsync()` automatically upserts all project glossary entries into the global glossary, ensuring subsequent translation projects can immediately reuse them.

#### Term Bank Import/Export

- **Auto-import at startup**: `App.xaml.cs` attempts to import pre-built term bank files from the `_ref_trans/doc/` directory on startup
- **Settings UI management**: `SettingsWindow` provides "Import Term Bank..." and "Export Term Bank..." buttons for manual glossary management
- **Glossary window**: `GlossaryWindow` (opened via 菜单 → 编辑 → 术语库管理...) backed by `GlossaryViewModel` lets you browse, search, add, edit, delete, import and export glossary terms in-app; inline edits commit back to the SQLite store per-row
- **Entry source tracking**: `TranslationGlossaryEntry.EntrySource` records origin (Imported/User/AutoFromCache), `ModifiedAt` records modification time

#### Key Interfaces

| Interface | Implementation | Location |
|-----------|---------------|----------|
| `IGlobalGlossaryStore` | `SqliteGlobalGlossaryStore` | `Infrastructure/Translation/` |
| `ITranslationService` | `TranslationService` | `Application/Services/` |
| `ITranslationEngine` | `GoogleTranslationEngine` | `Infrastructure/Translation/` |
| `ITranslationEngine` | `OpenAiTranslationEngine` | `Infrastructure/Translation/` |
| `ITranslationSourceReader` | `TranslationSourceReader` | `Infrastructure/Translation/` |
| `ITranslationPatchWriter` | `TranslationPatchWriter` | `Infrastructure/Translation/` |

#### Key Models

| Model | Purpose |
|-------|---------|
| `TranslationProgressDocument` | Top-level project state (ProjectKey, Files, ProviderSettings, OutputDirectory) |
| `TranslationFileState` | Per-file scan state (RelativePath, SourceFingerprint, GenerationMode, Entries) |
| `TranslationEntryState` | Per-entry state (Path, Original, OriginalHash, Translated, Status) |
| `TranslationGlossaryEntry` | Glossary entry (Source, Target, EntrySource, ModifiedAt, Category, Notes) |
| `TranslationEntryStatus` | Enum: Pending=0, Translated=1, Verified=2, Skipped=3, Failed=4 |
| `TranslationGenerationMode` | Enum: Auto=0, FileOverwrite=1, Patch=2 |
