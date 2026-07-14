# StarPakExplorer — AI Agent Instructions

## Quick Start

```powershell
dotnet build .\StarPakExplorer.sln
dotnet run --project .\StarPakExplorer.UI\StarPakExplorer.UI.csproj
```

**Stack**: .NET 8, Windows-only WPF (`net8.0-windows`), C# 12, Nullable enabled, ImplicitUsings.  
**Purpose**: Starbound mod `.pak` explorer, editor, patch manager, AI translation tool.

---

## Architecture (3-Layer, Manual DI)

```
UI (WPF)        → ViewModels, 7 Windows, Commands
Application     → 15 interfaces, 30+ models, 4 orchestrators
Infrastructure  → All implementations (cache, files, translation, unpacking, indexing, settings, logging, patches)
```

- **No DI container** — manual wiring in `App.xaml.cs` `OnStartup()`.
- All classes are **`sealed`**. No inheritance beyond `ViewModelBase`.
- `Application` → no external refs. `Infrastructure` → `Application`. `UI` → both.

→ Full details: [`docs/en/architecture.md`](docs/en/architecture.md) ([中文](docs/zh/architecture.md))

---

## Core Conventions

| Concern | Convention |
|---------|------------|
| Namespaces | `StarPakExplorer.{Layer}.{Feature}` (file-scoped) |
| C# version | C# 12, ImplicitUsings, Nullable enabled |
| Sealing | **All classes `sealed`** (services, ViewModels, models) |
| Model mutability | `{ get; set; }` mutable, `{ get; init; }` for immutable-after-creation |
| DI | Manual — wire everything in `App.xaml.cs::OnStartup()`, no container |
| Settings | `%LOCALAPPDATA%\StarPakExplorer\settings.json` |
| Logging | `%LOCALAPPDATA%\StarPakExplorer\Logs\app.log` (`FileAppLogger`) |
| Cache | `%LOCALAPPDATA%\StarPakExplorer\Cache\{sha256[:32]}\unpacked\` |
| Patches | `%LOCALAPPDATA%\StarPakExplorer\Patches\{patchKey}\` |
| Translation | `%LOCALAPPDATA%\StarPakExplorer\Translations\{projectKey}\` |
| UI Language | **Chinese** — all labels, status messages, button text |
| Cancellation | Every async method accepts `CancellationToken` |
| File paths | `Path.Combine()`, `Path.GetRelativePath()`, normalized `/` |

---

## Key Coding Patterns

### ViewModels
- Extend `ViewModelBase`. Properties: `{ get => field; set => SetProperty(ref field, value); }`
- Commands: `RelayCommand` (sync) / `AsyncRelayCommand` (async). Both in `UI/Commands/`.
- Async init: `_ = InitializeAsync();` fire-and-forget in constructor.
- Receive **all dependencies via constructor**. Named `{Feature}ViewModel`.

### Window ↔ ViewModel
```csharp
public MyWindow(MyViewModel vm) { DataContext = vm; InitializeComponent(); }
vm.RequestClose?.Invoke(true);
```
→ Full details: [`docs/en/viewmodels-windows.md`](docs/en/viewmodels-windows.md) ([中文](docs/zh/viewmodels-windows.md))

### Services
- All async: `ConfigureAwait(false)`, `CancellationToken`, `IProgress<string>?` for progress.
- External processes: `Process.Start()` + `WaitForExitAsync()`.
- Defined as interfaces in `Application/Abstractions/`, implemented `sealed` in `Infrastructure/{Feature}/`.

### Models
- Simple `{ get; set; }` POCOs under `Application/Models/`. All `sealed`.

---

## Pitfalls

- ❌ Do **not** add `Microsoft.Extensions.DependencyInjection` — manual DI by design.
- `MainWindow.SetTranslationServices()` must be called after construction.
- Do not introduce inheritance beyond `ViewModelBase`. No abstract services.
- All async methods must propagate `CancellationToken`.
- Chinese paths in `pakParentDirectory` may break `asset_unpacker.exe`.
- `TranslationTextTools` is a **stub** — all methods throw `NotImplementedException`.
- `FileStagingStore` implementation may not exist — check `Infrastructure/Files/` first.

---

## External Dependencies

| Dependency | Usage |
|------------|-------|
| `asset_unpacker.exe` / `asset_packer.exe` (Starbound) | PAK unpack/repack via `Process.Start` |
| Google Cloud Translation API v3 | Service account JWT auth, glossary support |
| OpenAI API (chat/completions) | Alternative engine, default `gpt-4o-mini` |

---

## Docs Index

| Doc (EN) | Doc (中文) | Content |
|-----------|------------|---------|
| [`docs/en/architecture.md`](docs/en/architecture.md) | [`docs/zh/architecture.md`](docs/zh/architecture.md) | Full architecture, DI chain, interface→impl mapping, storage layout |
| [`docs/en/feature-domains.md`](docs/en/feature-domains.md) | [`docs/zh/feature-domains.md`](docs/zh/feature-domains.md) | PAK loading, cache, file browsing, patches, pack export, settings |
| [`docs/en/translation-pipeline.md`](docs/en/translation-pipeline.md) | [`docs/zh/translation-pipeline.md`](docs/zh/translation-pipeline.md) | Both translation workflows, 4-stage pipeline, engine interface, models |
| [`docs/en/models.md`](docs/en/models.md) | [`docs/zh/models.md`](docs/zh/models.md) | Complete model catalog by domain |
| [`docs/en/viewmodels-windows.md`](docs/en/viewmodels-windows.md) | [`docs/zh/viewmodels-windows.md`](docs/zh/viewmodels-windows.md) | VM→Window mapping, MVVM patterns, base classes |
| [`docs/en/release-workflow.md`](docs/en/release-workflow.md) | [`docs/zh/release-workflow.md`](docs/zh/release-workflow.md) | Tag-triggered GitHub Actions release process |

---

## Related Repos

- `_ref_trans/` — Node.js auto-translator tool with glossary (separate project)
- `artifacts/_pack_src/` — older/alternative version of the solution


