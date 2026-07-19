# 功能域

## PAK 加载 (缓存 → 解包 → 索引)

**流程**: `PakExplorerService.LoadPakAsync()`:

1. 计算 `cacheKey = SHA256(pakPath)[:32]`
2. 尝试 `cacheRepository.TryLoadManifestAsync(cacheKey)` → 缓存命中则立即返回
3. 缓存未命中: `cacheRepository.PrepareFreshCacheAsync()` → `assetUnpacker.UnpackAsync()` (通过 `Process.Start` 调用外部 `asset_unpacker.exe`)
4. `metadataReader.ReadAsync()` → 解析 `_metadata` 或 `.metadata` JSON
5. `fileIndexService.BuildIndex()` → `Directory.EnumerateFiles()` 遍历所有解包文件至 `List<ResourceFileRecord>`
6. 将 `PakManifest` JSON 保存至缓存，返回 `PakLoadResult`

**关键角色**:

| 角色 | 接口 → 实现 | 位置 |
|------|---------------------------|----------|
| 解包 | `IAssetUnpacker` → `AssetUnpacker` | `Infrastructure/Unpacking/` |
| 缓存 | `ICacheRepository` → `CacheRepository` | `Infrastructure/Cache/` |
| 元数据 | `IMetadataReader` → `MetadataReader` | `Infrastructure/Metadata/` |
| 索引 | `IFileIndexService` → `FileIndexService` | `Infrastructure/Indexing/` |

## 缓存管理

- **位置**: `%LOCALAPPDATA%\StarPakExplorer\Cache\{sha256[:32]}\unpacked\`
- `CacheRepository` 计算 pakPath 的 SHA256，取前 32 个十六进制字符作为键
- 缓存根目录存储 `manifest.json`；`unpacked/` 子目录存放解压后的文件
- 主窗口展示缓存概览，支持删除/清空操作

`ICacheRepository` 方法: `GetCacheKey()`, `TryLoadManifestAsync()`, `PrepareFreshCacheAsync()`, `SaveManifestAsync()`, `GetUnpackedDirectory()`, `GetOverviewAsync()`, `DeleteAsync()`, `ClearAllAsync()`

## 文件浏览与搜索

### 文件分类标签 (MainWindow 11 个标签页)

全部 → 元数据 → 物品 → 对象 → NPC与怪物 → 生物群系与世界生成 → 界面 → 纹理与动画 → 脚本 → 音频 → 补丁 → 其他

分类: `StarboundFileClassifier` 将文件扩展名映射到 `FileCategory` 和 `StarboundFileSection`。

### 预览

`PakExplorerService.GetPreviewAsync()` → `FilePreview` (Text/Image/Binary)。  
`ITextFileReader` 处理最大 1MB 的文本文件，最大 12MB 的图片。

### 搜索

`PakExplorerService.SearchAsync()` — 在文本文件内容中按关键词搜索 (每文件最大 2MB)，返回 `List<SearchHit>`。

### 重名扫描

`PakExplorerService.ScanDuplicateItemNamesAsync()` — 扫描 `.item`/`.activeitem`/`.object`/`.matitem` 中的重复 `itemName` 字段，返回 `List<DuplicateItemNameResult>`。

## 补丁管理

补丁是应用在原模组文件之上的文本/二进制修改。

**流程**:
1. 在 MainWindow 中选择文件 → 双击 → 打开 `FileModifyWindow`
2. `FileModifyViewModel` 加载文件内容，支持文本编辑（含编码选择）或二进制替换
3. 保存时 → `IPatchStore.SaveTextAsync()` 或 `SaveReplacementAsync()` → 写入 `%LOCALAPPDATA%\StarPakExplorer\Patches\{patchKey}/`
4. `PatchManagerWindow` 展示所有补丁集，支持打包为 .pak 导出

**补丁键**: 创意工坊模组使用 `workshop_{id}`，否则使用 `{name}_{hash}`。

**核心接口**: `IPatchStore` → `PatchStore` (`Infrastructure/Patches/`)  
方法: `GetPatchRoot()`, `GetPatchKey()`, `EnsurePatchSetAsync()`, `SaveTextAsync()`, `SaveReplacementAsync()`, `GetPatchSetsAsync()`, `GetPatchFilesAsync()`, `DeleteAsync()`

**文件暂存**: `IFileStagingStore` → `FileStagingStore` — 修改后文件在成为补丁之前的暂存区域。

## 打包导出 (`PackManagerWindow`)

`PackManagerViewModel` → 浏览源目录 → 树形视图 (`PackTreeNodeViewModel`) → 选择文件 → `ExportCommand` → `IAssetPacker.PackAsync()` 调用外部 `asset_packer.exe`。

## 设置

- **位置**: `%LOCALAPPDATA%\StarPakExplorer\settings.json`
- **存储**: `IAppSettingsStore` → `JsonAppSettingsStore` (JSON 序列化)

**`AppSettings`** (7 个字符串属性):

| 属性 | 用途 |
|----------|---------|
| `AssetUnpackerPath` | Starbound `asset_unpacker.exe` 路径 |
| `AssetPackerPath` | Starbound `asset_packer.exe` 路径 |
| `PakParentDirectory` | `.pak` 文件选择的默认目录 |
| `PatchRootDirectory` | 自定义补丁存储根目录 (默认: `%LOCALAPPDATA%\StarPakExplorer\Patches`) |
| `CacheRootDirectory` | 自定义缓存根目录 (默认: `%LOCALAPPDATA%\StarPakExplorer\Cache`) |
| `TranslationRootDirectory` | 自定义翻译项目根目录 (默认: `%LOCALAPPDATA%\StarPakExplorer\Translations`) |
| `GlobalGlossaryPath` | 全局术语表路径 (默认留空: 使用 `<安装目录>\global_glossary.json`) |

## 翻译流水线与术语表

翻译系统支持两种工作流：独立翻译（`TranslationWindow`）和完整项目流水线（`TranslationManagerWindow`）。详见 [translation-pipeline.md](translation-pipeline.md)。

### 项目流水线概览

**四阶段流程**: 创建项目 → 扫描 → 翻译 → 生成

1. **创建/加载**: `TranslationService.LoadOrCreateProjectAsync()` → `TranslationProgressDocument` 持久化至 `%LOCALAPPDATA%\StarPakExplorer\Translations\{projectKey}/`
2. **扫描**: `TranslationService.ScanAsync()` 枚举 `.item`/`.activeitem`/`.object`/`.matitem`/`.codex` 文件，提取 `shortdescription`、`description` 及 8 种种族描述字段
3. **翻译**: `TranslationService.TranslatePendingAsync()` 批量翻译（每批 30 条），支持 Google Cloud Translation API v3 和 OpenAI API 两种引擎
4. **生成**: `TranslationService.GenerateOutputAsync()` 输出 `.patch` 文件 + `_metadata`

### 术语表系统

翻译系统使用双层术语表架构，确保译文一致性：

#### 第一层：项目术语表

每个翻译项目维护独立的项目级术语表，存储在项目目录下的 `glossary.json`。项目术语表的条目仅在该项目内生效，允许不同模组使用不同的术语映射。

#### 第二层：全局术语表 (新增)

**位置**: `<安装目录>\global_glossary.json`（可在设置中自定义路径）

**接口**: `IGlobalGlossaryStore` → `GlobalGlossaryStore` (`Infrastructure/Translation/`)

核心方法:
- `LoadAllAsync()` / `SaveAllAsync()` — 加载/保存全局术语表
- `UpsertAsync(key, value)` — 添加或更新单个条目
- `DeleteAsync(key)` — 删除条目
- `ImportFromFileAsync(path)` — 从外部术语库文件导入（支持 `英文|||中文` 格式）
- `ExportToFileAsync(path)` — 导出术语表到文件
- `BuildLookupAsync()` — 构建 `Dictionary<string, string>` 查询表

#### 术语表合并策略

`TranslationService.EnsureGlossaryAsync()` 在每次翻译前按以下优先级合并术语表：

1. **项目术语表** — 最高优先级
2. **全局术语表** — 作为兜底补充
3. **内置默认术语表** — `TranslationTextTools.BuildDefaultGlossary()` 提供约 40 个星界边境常用术语（矿物: Copper/铜、Iron/铁、Gold/金、Titanium/钛 等；种族: Floran/叶族、Hylotl/鲛人、Avian/翼族 等）

#### 翻译后同步

每次翻译完成后，`TranslationService.SyncToGlobalGlossaryAsync()` 自动将项目术语表中的所有条目同步（Upsert）至全局术语表，确保后续翻译项目可直接复用。

#### 术语库导入导出

- **启动时自动导入**: `App.xaml.cs` 在启动时尝试从 `_ref_trans/doc/` 目录导入预置的术语库文件（`星界边境术语库-英中.txt` 等）
- **设置界面管理**: `SettingsWindow` 提供「从术语库导入...」和「导出术语库...」按钮，用户可手动管理术语表
- **条目来源追踪**: `TranslationGlossaryEntry.EntrySource` 记录来源 (Imported/User/AutoFromCache)，`ModifiedAt` 记录修改时间

#### 关键接口

| 接口 | 实现 | 位置 |
|-----------|---------------|----------|
| `IGlobalGlossaryStore` | `GlobalGlossaryStore` | `Infrastructure/Translation/` |
| `ITranslationService` | `TranslationService` | `Application/Services/` |
| `ITranslationEngine` | `GoogleTranslationEngine` | `Infrastructure/Translation/` |
| `ITranslationEngine` | `OpenAiTranslationEngine` | `Infrastructure/Translation/` |
| `ITranslationSourceReader` | `TranslationSourceReader` | `Infrastructure/Translation/` |
| `ITranslationPatchWriter` | `TranslationPatchWriter` | `Infrastructure/Translation/` |

#### 关键模型

| 模型 | 用途 |
|-------|---------|
| `TranslationProgressDocument` | 项目顶层状态 (ProjectKey, Files, ProviderSettings, OutputDirectory) |
| `TranslationFileState` | 每文件扫描状态 (RelativePath, SourceFingerprint, GenerationMode, Entries) |
| `TranslationEntryState` | 每条目状态 (Path, Original, OriginalHash, Translated, Status) |
| `TranslationGlossaryEntry` | 术语表条目 (Source, Target, EntrySource, ModifiedAt, Category, Notes) |
| `TranslationEntryStatus` | 枚举: Pending=0, Translated=1, Verified=2, Skipped=3, Failed=4 |
| `TranslationGenerationMode` | 枚举: Auto=0, FileOverwrite=1, Patch=2 |
