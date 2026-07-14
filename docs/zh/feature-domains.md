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

**`AppSettings`** (6 个字符串属性):

| 属性 | 用途 |
|----------|---------|
| `AssetUnpackerPath` | Starbound `asset_unpacker.exe` 路径 |
| `AssetPackerPath` | Starbound `asset_packer.exe` 路径 |
| `PakParentDirectory` | `.pak` 文件选择的默认目录 |
| `PatchRootDirectory` | 自定义补丁存储根目录 (默认: `%LOCALAPPDATA%\StarPakExplorer\Patches`) |
| `CacheRootDirectory` | 自定义缓存根目录 (默认: `%LOCALAPPDATA%\StarPakExplorer\Cache`) |
| `TranslationRootDirectory` | 自定义翻译项目根目录 (默认: `%LOCALAPPDATA%\StarPakExplorer\Translations`) |
