# ViewModels & Windows

## ViewModel → Window Mapping

| ViewModel | Window | Purpose |
|-----------|--------|---------|
| `MainViewModel` | `MainWindow` | PAK loading, file browsing, preview, search, duplicate scan |
| `TranslationViewModel` | `TranslationWindow` | Standalone manual translation (pick folder → translate → export patches) |
| `TranslationManagerViewModel` | `TranslationManagerWindow` | Full pipeline: Scan → AI Translate → Generate (project-based) |
| `SettingsViewModel` | `SettingsWindow` | Configure external tool paths and storage directories |
| `PackManagerViewModel` | `PackManagerWindow` | Folder tree → select files → export .pak |
| `PatchManagerViewModel` | `PatchManagerWindow` | Browse/delete/pack patch sets |
| `FileModifyViewModel` | `FileModifyWindow` | Text/binary file editing with encoding selection |

## Window ↔ ViewModel Pattern

Window constructor receives ViewModel, sets `DataContext` before `InitializeComponent()`:

```csharp
public TranslationWindow(TranslationViewModel viewModel)
{
    DataContext = viewModel;
    InitializeComponent();
}
```

ViewModel closes its window via event:

```csharp
viewModel.RequestClose?.Invoke(true);
```

## Base Classes & Commands

| Class | Location | Purpose |
|-------|----------|---------|
| `ViewModelBase` | `UI/ViewModels/` | Abstract base: `SetProperty<T>(ref T, T, [CallerMemberName])`, `OnPropertyChanged()` |
| `RelayCommand` | `UI/Commands/` | `ICommand` for sync actions, `Func<bool>?` canExecute |
| `AsyncRelayCommand` | `UI/Commands/` | `ICommand` for async actions, `isExecuting` guard, `RaiseCanExecuteChanged()` |

## MVVM Conventions

- Every ViewModel extends `ViewModelBase`.
- Properties: `{ get => field; set => SetProperty(ref field, value); }`
- Commands: `RelayCommand` (sync) / `AsyncRelayCommand` (async).
- Async init: `_ = InitializeAsync();` fire-and-forget in constructors.
- ViewModels receive **all dependencies via constructor**.
- Named `{Feature}ViewModel`, under `StarPakExplorer.UI.ViewModels`.
