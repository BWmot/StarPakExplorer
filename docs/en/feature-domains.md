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

**`AppSettings`** (6 string properties):

| Property | Purpose |
|----------|---------|
| `AssetUnpackerPath` | Path to Starbound's `asset_unpacker.exe` |
| `AssetPackerPath` | Path to Starbound's `asset_packer.exe` |
| `PakParentDirectory` | Default directory for `.pak` file selection |
| `PatchRootDirectory` | Custom patch storage root (default: `%LOCALAPPDATA%\StarPakExplorer\Patches`) |
| `CacheRootDirectory` | Custom cache root (default: `%LOCALAPPDATA%\StarPakExplorer\Cache`) |
| `TranslationRootDirectory` | Custom translation project root (default: `%LOCALAPPDATA%\StarPakExplorer\Translations`) |
