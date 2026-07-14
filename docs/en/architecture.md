# Architecture

## 3-Layer Structure

```
UI (WPF)        → ViewModels (MVVM), 7 Windows, Commands (RelayCommand / AsyncRelayCommand)
Application     → Abstractions (15 interfaces), Models (30+ POCOs), Services (4 orchestrators)
Infrastructure  → Implements all abstractions (cache, files, translation, unpacking, indexing, settings, logging, patches)
```

- **No DI container** — everything wired manually in `App.xaml.cs` `OnStartup()`.
- All services and ViewModels are **`sealed`**. No inheritance beyond `ViewModelBase`.
- `Application` layer references nothing external. `Infrastructure` → `Application`. `UI` → both.

## Dependency Chain (App.xaml.cs OnStartup)

```
FileAppLogger (IAppLogger)
  → JsonAppSettingsStore (IAppSettingsStore)
    → AppSettings (shared instance, injected everywhere)
      → ICacheRepository → CacheRepository
      → IPatchStore → PatchStore
      → GoogleTranslationEngine + OpenAiTranslationEngine (ITranslationEngine)
        → TranslationService (ITranslationService)
          → TranslationManagerViewModel
        → PakExplorerService (unpacker, packer, cache, patches, metadata, indexing, text reader)
          → MainViewModel → MainWindow
```

## Complete Interface → Implementation Mapping

| Interface | Implementation | Infrastructure Path |
|-----------|---------------|---------------------|
| `IAppLogger` | `FileAppLogger` | `Logging/FileAppLogger.cs` |
| `IAppSettingsStore` | `JsonAppSettingsStore` | `Settings/JsonAppSettingsStore.cs` |
| `IAssetPacker` | `AssetPacker` | `Unpacking/AssetPacker.cs` |
| `IAssetUnpacker` | `AssetUnpacker` | `Unpacking/AssetUnpacker.cs` |
| `ICacheRepository` | `CacheRepository` | `Cache/CacheRepository.cs` |
| `IFileIndexService` | `FileIndexService` | `Indexing/FileIndexService.cs` |
| `IFileStagingStore` | `FileStagingStore` | `Files/FileStagingStore.cs` |
| `IMetadataReader` | `MetadataReader` | `Metadata/MetadataReader.cs` |
| `IPatchStore` | `PatchStore` | `Patches/PatchStore.cs` |
| `ITextFileReader` | `TextFileReader` | `Files/TextFileReader.cs` |
| `ITranslationEngine` | `GoogleTranslationEngine` | `Translation/GoogleTranslationEngine.cs` |
| `ITranslationEngine` | `OpenAiTranslationEngine` | `Translation/OpenAiTranslationEngine.cs` |
| `ITranslationPatchWriter` | `TranslationPatchWriter` | `Translation/TranslationPatchWriter.cs` |
| `ITranslationProjectStore` | `TranslationProjectStore` | `Translation/TranslationProjectStore.cs` |
| `ITranslationSourceReader` | `TranslationSourceReader` | `Translation/TranslationSourceReader.cs` |

## Orchestrators (Application/Services)

| Service | Responsibility |
|---------|---------------|
| `PakExplorerService` | PAK loading, preview, search, duplicate scan, file staging |
| `TranslationService` | Full translation pipeline orchestration |
| `TranslationTextTools` | Static utility (currently stub — `NotImplementedException`) |
| `WorkshopIdExtractor` | Extracts Steam workshop ID from path |

## External Dependencies

| Dependency | Usage |
|------------|-------|
| `asset_unpacker.exe` (Starbound) | Unpacks `.pak` files; called via `Process.Start` in `AssetUnpacker` |
| `asset_packer.exe` (Starbound) | Repacks folders into `.pak`; called via `Process.Start` in `AssetPacker` |
| Google Cloud Translation API v3 | Batch translation with service account JWT auth; glossary support |
| OpenAI API (chat/completions) | Alternative translation engine; default model `gpt-4o-mini` |

## Storage Layout

| Purpose | Path |
|---------|------|
| Settings | `%LOCALAPPDATA%\StarPakExplorer\settings.json` |
| Logging | `%LOCALAPPDATA%\StarPakExplorer\Logs\app.log` |
| Cache | `%LOCALAPPDATA%\StarPakExplorer\Cache\{sha256[:32]}\unpacked\` |
| Patches | `%LOCALAPPDATA%\StarPakExplorer\Patches\{patchKey}\` |
| Translation | `%LOCALAPPDATA%\StarPakExplorer\Translations\{projectKey}\` |
