# 架构

## 三层结构

```
UI (WPF)        → ViewModels (MVVM), 7 个窗口, Commands (RelayCommand / AsyncRelayCommand)
Application     → 抽象接口 (15 个), 数据模型 (30+ POCO), 服务 (4 个编排器)
Infrastructure  → 所有接口实现 (缓存、文件、翻译、解包、索引、设置、日志、补丁)
```

- **无 DI 容器** — 所有依赖在 `App.xaml.cs` `OnStartup()` 中手动装配。
- 所有服务和 ViewModel 均为 **`sealed`**。除 `ViewModelBase` 外无继承。
- `Application` 层不引用任何外部依赖。`Infrastructure` → `Application`。`UI` → 两者。

## 依赖链 (App.xaml.cs OnStartup)

```
FileAppLogger (IAppLogger)
  → JsonAppSettingsStore (IAppSettingsStore)
    → AppSettings (共享实例, 注入各处)
      → ICacheRepository → CacheRepository
      → IPatchStore → PatchStore
      → GoogleTranslationEngine + OpenAiTranslationEngine (ITranslationEngine)
        → TranslationService (ITranslationService)
          → TranslationManagerViewModel
        → PakExplorerService (解包, 打包, 缓存, 补丁, 元数据, 索引, 文本读取)
          → MainViewModel → MainWindow
```

## 完整接口 → 实现映射

| 接口 | 实现 | Infrastructure 路径 |
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

## 编排器 (Application/Services)

| 服务 | 职责 |
|---------|---------------|
| `PakExplorerService` | PAK 加载、预览、搜索、重名扫描、文件暂存 |
| `TranslationService` | 完整翻译流水线编排 |
| `TranslationTextTools` | 静态工具类 (当前为桩代码 — `NotImplementedException`) |
| `WorkshopIdExtractor` | 从路径提取 Steam 创意工坊 ID |

## 外部依赖

| 依赖 | 用途 |
|------------|-------|
| `asset_unpacker.exe` (Starbound) | 解包 `.pak` 文件；在 `AssetUnpacker` 中通过 `Process.Start` 调用 |
| `asset_packer.exe` (Starbound) | 将文件夹打包为 `.pak`；在 `AssetPacker` 中通过 `Process.Start` 调用 |
| Google Cloud Translation API v3 | 使用服务账号 JWT 认证进行批量翻译；支持术语表 |
| OpenAI API (chat/completions) | 替代翻译引擎；默认模型 `gpt-4o-mini` |

## 存储布局

| 用途 | 路径 |
|---------|------|
| 设置 | `%LOCALAPPDATA%\StarPakExplorer\settings.json` |
| 日志 | `%LOCALAPPDATA%\StarPakExplorer\Logs\app.log` |
| 缓存 | `%LOCALAPPDATA%\StarPakExplorer\Cache\{sha256[:32]}\unpacked\` |
| 补丁 | `%LOCALAPPDATA%\StarPakExplorer\Patches\{patchKey}\` |
| 翻译 | `%LOCALAPPDATA%\StarPakExplorer\Translations\{projectKey}\` |
