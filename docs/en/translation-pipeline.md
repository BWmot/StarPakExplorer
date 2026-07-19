# Translation Pipeline

Two translation workflows exist side-by-side.

## A. Standalone Translation (`TranslationViewModel` + `TranslationWindow`)

Simpler, manual workflow:

1. User picks an already-unpacked mod folder + output path
2. Scans for `.item` / `.activeitem` / `.object` / `.matitem` / `.codex` files
3. Extracts translatable fields:
   - `shortdescription`
   - `description`
   - 8 race descriptions: `apexDescription`, `avianDescription`, `floranDescription`, `glitchDescription`, `humanDescription`, `hylotlDescription`, `novakidDescription`, `feneroxDescription`
4. User manually enters translations per field
5. Exports as `.patch` files + `_metadata`

## B. Full Translation Pipeline (`TranslationManagerViewModel` + `TranslationManagerWindow`)

Project-based, 4-stage pipeline.

### Stage 1 — Create/Load Project

`TranslationService.LoadOrCreateProjectAsync()` → `TranslationProgressDocument` persisted as JSON.

- Project key: `CN_{ModName}_zhCN`
- Location: `%LOCALAPPDATA%\StarPakExplorer\Translations\{projectKey}/`

### Stage 2 — Scan

`TranslationService.ScanAsync()`:
- Enumerates translatable files
- Generates `TranslationFileState` with `TranslationEntryState` per entry
- Checks source file fingerprint (SHA256) to detect changes vs previous scan
- Each file gets a `TranslationGenerationMode` suggestion: Auto / FileOverwrite / Patch

### Stage 3 — Translate

`TranslationService.TranslatePendingAsync()`:
- Batch-translates all pending entries via selected engine
- Batch size: 30 entries per request
- Results cached in `translations_cache.json` / `file_translations.json`

#### Google Cloud Translation API v3 (`GoogleTranslationEngine`)
- Uses service account JWT authentication
- Supports glossary (bidirectional term mapping)
- Settings: `ProjectId`, `Location`, `ServiceAccountJsonPath`, `GlossaryName`

#### OpenAI API (`OpenAiTranslationEngine`)
- Uses `chat/completions` endpoint
- System prompt provides game-translation context
- Default model: `gpt-4o-mini`
- Settings: `ApiKey`, `Model`, `BaseUrl`

### Stage 4 — Generate

`TranslationService.GenerateOutputAsync()`:
- Writes `.patch` files and `_metadata` to output directory
- `ITranslationPatchWriter` produces JSON Patch operations:
  ```json
  [{ "op": "replace", "path": "/shortdescription", "value": "翻译" }]
  ```
- Generates `_metadata` with `requires: [originalModName]`

## Translation Engine Interface

```csharp
public interface ITranslationEngine
{
    TranslationEngineType EngineType { get; }
    Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> sourceTexts,
        TranslationProviderSettings settings,
        IReadOnlyDictionary<string, string> glossary,
        CancellationToken cancellationToken);
}
```

## Key Interfaces

| Interface | Implementation | Location |
|-----------|---------------|----------|
| `ITranslationService` | `TranslationService` | `Application/Services/` |
| `ITranslationProjectStore` | `TranslationProjectStore` | `Infrastructure/Translation/` |
| `ITranslationEngine` | `GoogleTranslationEngine` | `Infrastructure/Translation/` |
| `ITranslationEngine` | `OpenAiTranslationEngine` | `Infrastructure/Translation/` |
| `ITranslationSourceReader` | `TranslationSourceReader` | `Infrastructure/Translation/` |
| `ITranslationPatchWriter` | `TranslationPatchWriter` | `Infrastructure/Translation/` |

## Key Translation Models

| Model | Purpose |
|-------|---------|
| `TranslationProgressDocument` | Top-level project state (ProjectKey, Files, ProviderSettings, OutputDirectory) |
| `TranslationFileState` | Per-file scan state (RelativePath, SourceFingerprint, GenerationMode, Entries, IsSelected) |
| `TranslationEntryState` | Per-entry state (Path, Original, OriginalHash, Translated, Status, IsManuallyEdited) |
| `TranslationEntryStatus` | Enum: Pending=0, Translated=1, Verified=2, Skipped=3, Failed=4 |
| `TranslationGenerationMode` | Enum: Auto=0, FileOverwrite=1, Patch=2 |
| `TranslationProviderSettings` | PreferredEngine + OpenAi + Google settings |
| `TranslationModMetadata` | Output mod metadata (Version, Author, ModName, FriendlyName, Description, Link, Priority) |

## Glossary System (New)

The translation system uses a dual-layer glossary architecture:

### Project Glossary

Each translation project maintains an independent `glossary.json` (stored in the project directory). Project glossary entries only take effect within that project.

### Global Glossary

**Storage**: `<install directory>\global_glossary.json` (customizable via `AppSettings.GlobalGlossaryPath`)

**Interface**: `IGlobalGlossaryStore` → `GlobalGlossaryStore` (`Infrastructure/Translation/GlobalGlossaryStore.cs`)

**Merge Strategy** — `TranslationService.EnsureGlossaryAsync()`:
1. Load project glossary (highest priority)
2. Merge global glossary as fallback (project entries are not overwritten)
3. If still empty, use `BuildDefaultGlossary()` with ~40 built-in common Starbound terms

**Auto Sync**: After each translation completes, `SyncToGlobalGlossaryAsync()` automatically upserts project glossary entries into the global glossary.

**Term Bank Import**: On startup, automatically imports pre-built term banks from `_ref_trans/doc/` (`English|||Chinese` format). Manual import/export also available via Settings UI.

**Entry Tracking**: `TranslationGlossaryEntry` records `EntrySource` (Imported/User/AutoFromCache), `ModifiedAt`, `Category`, `Notes`.
| `TranslatableEntry` | Source file entry (RelativePath, ItemName, FileType, SourceFields, TranslatedFields) |
| `TranslationSourceEntry` | Path, Original, TokenStartIndex, TokenEndIndex |
| `TranslationGlossaryEntry` | Source, Target |
| `TranslationEngineType` | Enum: Google=0, OpenAI=1 |
| `GoogleTranslationSettings` | ProjectId, Location, ServiceAccountJsonPath, GlossaryName |
| `OpenAiTranslationSettings` | ApiKey, Model, BaseUrl |
| `TranslationFileAnalysis` | Analysis result for a single file |
