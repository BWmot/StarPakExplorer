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
| `TranslationProviderSettings` | PreferredEngine + OpenAi + Google 设置 |
| `TranslationModMetadata` | 输出模组元数据 (Version, Author, ModName, FriendlyName, Description, Link, Priority) |
| `TranslatableEntry` | 源文件条目 (RelativePath, ItemName, FileType, SourceFields, TranslatedFields) |
| `TranslationSourceEntry` | Path, Original, TokenStartIndex, TokenEndIndex |
| `TranslationGlossaryEntry` | Source, Target |
| `TranslationEngineType` | 枚举: Google=0, OpenAI=1 |
| `GoogleTranslationSettings` | ProjectId, Location, ServiceAccountJsonPath, GlossaryName |
| `OpenAiTranslationSettings` | ApiKey, Model, BaseUrl |
| `TranslationFileAnalysis` | 单个文件的分析结果 |
