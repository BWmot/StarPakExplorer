# 翻译流水线

两套翻译工作流并行存在。

## A. 独立翻译 (`TranslationViewModel` + `TranslationWindow`)

较简单的半手动工作流：

1. 用户选择已解包的模组文件夹 + 输出路径
2. 扫描 `.item` / `.activeitem` / `.object` / `.matitem` / `.codex` 文件
3. 提取可翻译字段：
   - `shortdescription`
   - `description`
   - 8 种种族描述: `apexDescription`, `avianDescription`, `floranDescription`, `glitchDescription`, `humanDescription`, `hylotlDescription`, `novakidDescription`, `feneroxDescription`
4. 用户手动逐字段输入翻译
5. 导出为 `.patch` 文件 + `_metadata`

## B. 完整翻译流水线 (`TranslationManagerViewModel` + `TranslationManagerWindow`)

基于项目，四阶段流水线。

### 阶段 1 — 创建/加载项目

`TranslationService.LoadOrCreateProjectAsync()` → `TranslationProgressDocument` 以 JSON 持久化。

- 项目键: `CN_{ModName}_zhCN`
- 位置: `%LOCALAPPDATA%\StarPakExplorer\Translations\{projectKey}/`

### 阶段 2 — 扫描

`TranslationService.ScanAsync()`:
- 枚举可翻译文件
- 为每个条目生成 `TranslationFileState` 及 `TranslationEntryState`
- 检查源文件指纹 (SHA256) 以检测与上次扫描的变更
- 每个文件获得 `TranslationGenerationMode` 建议: Auto / FileOverwrite / Patch

### 阶段 3 — 翻译

`TranslationService.TranslatePendingAsync()`:
- 通过选定引擎批量翻译所有待处理条目
- 目标语言: 由项目 `ProviderSettings.TargetLanguage`（BCP-47，默认 `zh-CN`）决定，如 `zh-TW` / `ja` / `ko`；引擎按该语言产出译文，全局词库查询与翻译缓存均按该语言作用域，避免不同目标语言互相污染
- 批次大小: 每次请求 30 条
- 结果缓存于 `translations_cache.json` / `file_translations.json`

#### Google Cloud Translation API v3 (`GoogleTranslationEngine`)
- 使用服务账号 JWT 认证
- 支持术语表 (双向术语映射)
- 设置: `ProjectId`, `Location`, `ServiceAccountJsonPath`, `GlossaryName`

#### OpenAI API (`OpenAiTranslationEngine`)
- 使用 `chat/completions` 端点
- 系统提示提供游戏翻译上下文
- 默认模型: `gpt-4o-mini`
- 设置: `ApiKey`, `Model`, `BaseUrl`

### 阶段 4 — 生成

`TranslationService.GenerateOutputAsync()`:
- 将 `.patch` 文件和 `_metadata` 写入输出目录
- `ITranslationPatchWriter` 生成 JSON Patch 操作:
  ```json
  [{ "op": "replace", "path": "/shortdescription", "value": "翻译" }]
  ```
- 生成包含 `requires: [originalModName]` 的 `_metadata`

### 阶段 5 — 导入已有翻译（重复检查）

`TranslationService.ImportExistingTranslationsAsync()`:

- 在填入/变更输出目录后自动触发（也可通过“导入已有翻译”按钮手动触发）
- 扫描输出目录中已生成的 `.patch` 文件（或整文件覆盖结果）
- 解析其中的 `path → value` 映射，仅回填**尚未翻译**的条目（已翻译的条目不覆盖）
- 跳过原文即译文的条目；导入成功后自动保存项目并刷新界面

适用场景：同一模组已经翻译了一部分（如 `E:\Starbound\translate\SBR_zh`），
新项目指向该目录时自动沿用已有译文，避免重复翻译。

## 翻译引擎接口

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

引擎通过 `settings.TargetLanguage`（BCP-47）得知目标语言，不再硬编码简体中文；Google Cloud 透传为 `targetLanguageCode`，OpenAI 注入系统/用户提示，GoogleFree 透传为 `tl` 查询参数。

## 核心接口

| 接口 | 实现 | 位置 |
|-----------|---------------|----------|
| `ITranslationService` | `TranslationService` | `Application/Services/` |
| `ITranslationProjectStore` | `TranslationProjectStore` | `Infrastructure/Translation/` |
| `ITranslationEngine` | `GoogleTranslationEngine` | `Infrastructure/Translation/` |
| `ITranslationEngine` | `OpenAiTranslationEngine` | `Infrastructure/Translation/` |
| `ITranslationSourceReader` | `TranslationSourceReader` | `Infrastructure/Translation/` |
| `ITranslationPatchWriter` | `TranslationPatchWriter` | `Infrastructure/Translation/` |

## 核心翻译模型

| 模型 | 用途 |
|-------|---------|
| `TranslationProgressDocument` | 项目顶层状态 (ProjectKey, Files, ProviderSettings, OutputDirectory) |
| `TranslationFileState` | 每文件扫描状态 (RelativePath, SourceFingerprint, GenerationMode, Entries, IsSelected) |
| `TranslationEntryState` | 每条目状态 (Path, Original, OriginalHash, Translated, Status, IsManuallyEdited) |
| `TranslationEntryStatus` | 枚举: Pending=0, Translated=1, Verified=2, Skipped=3, Failed=4 |
| `TranslationGenerationMode` | 枚举: Auto=0, FileOverwrite=1, Patch=2 |
| `TranslationProviderSettings` | PreferredEngine + TargetLanguage(BCP-47, 默认 zh-CN) + OpenAi + Google 设置 |
| `TranslationModMetadata` | 输出模组元数据 (Version, Author, ModName, FriendlyName, Description, Link, Priority) |

## 术语表系统 (新增)

翻译系统采用双层术语表架构：

### 项目术语表

每个翻译项目维护独立的 `glossary.json`（存储于项目目录下）。项目术语表仅在该项目内生效。

### 全局术语表

**存储**: SQLite 数据库，位于 `<安装目录>\global_glossary.db`（可通过 `AppSettings.GlobalGlossaryPath` 自定义）。基于 `Microsoft.Data.Sqlite`；首次启动时旧版 `global_glossary.json` 自动迁移为数据库（重命名为 `global_glossary.json.migrated`）。

**接口**: `IGlobalGlossaryStore` → `SqliteGlobalGlossaryStore` (`Infrastructure/Translation/SqliteGlobalGlossaryStore.cs`)。支持 `SearchAsync`（LIKE 模糊搜索、不区分大小写）、批量 `UpsertManyAsync`/`DeleteManyAsync`、`CountAsync`、导入导出及查询表构建。

**合并策略** — `TranslationService.EnsureGlossaryAsync()`:
1. 加载项目术语表（最高优先级）
2. 合并全局术语表作为兜底（项目已有条目不被覆盖）
3. 若仍为空，使用 `BuildDefaultGlossary()` 内置的约 40 个星界边境常用术语

**自动同步**: 每次翻译完成后，`SyncToGlobalGlossaryAsync()` 自动将项目术语表条目 Upsert 至全局术语表。`TranslateSingleAsync` 在单条翻译成功后会把新译文写入项目术语表，因此新翻译的条目也会随同步自动进入全局术语表（标记为 `AutoFromCache`），供后续项目复用。

**术语库导入**: 启动时自动从 `_ref_trans/doc/` 导入预置术语库（`英文|||中文` 格式）。也可通过设置界面手动导入/导出。

**条目追踪**: `TranslationGlossaryEntry` 记录 `EntrySource` (Imported/User/AutoFromCache)、`ModifiedAt`、`Category`、`Notes`。
| `TranslatableEntry` | 源文件条目 (RelativePath, ItemName, FileType, SourceFields, TranslatedFields) |
| `TranslationSourceEntry` | Path, Original, TokenStartIndex, TokenEndIndex |
| `TranslationGlossaryEntry` | Source, Target, Language (BCP-47, default zh-CN) |
| `TranslationEngineType` | 枚举: Google=0, OpenAI=1 |
| `GoogleTranslationSettings` | ProjectId, Location, ServiceAccountJsonPath, GlossaryName |
| `OpenAiTranslationSettings` | ApiKey, Model, BaseUrl |
| `TranslationFileAnalysis` | 单个文件的分析结果 |
