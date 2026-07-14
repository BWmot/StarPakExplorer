# ViewModels 与窗口

## ViewModel → 窗口映射

| ViewModel | 窗口 | 用途 |
|-----------|--------|---------|
| `MainViewModel` | `MainWindow` | PAK 加载、文件浏览、预览、搜索、重名扫描 |
| `TranslationViewModel` | `TranslationWindow` | 独立手动翻译 (选择文件夹 → 翻译 → 导出补丁) |
| `TranslationManagerViewModel` | `TranslationManagerWindow` | 完整流水线: 扫描 → AI 翻译 → 生成 (基于项目) |
| `SettingsViewModel` | `SettingsWindow` | 配置外部工具路径和存储目录 |
| `PackManagerViewModel` | `PackManagerWindow` | 文件夹树 → 选择文件 → 导出 .pak |
| `PatchManagerViewModel` | `PatchManagerWindow` | 浏览/删除/打包补丁集 |
| `FileModifyViewModel` | `FileModifyWindow` | 带编码选择的文本/二进制文件编辑 |

## 窗口 ↔ ViewModel 模式

窗口构造函数接收 ViewModel，在 `InitializeComponent()` 之前设置 `DataContext`：

```csharp
public TranslationWindow(TranslationViewModel viewModel)
{
    DataContext = viewModel;
    InitializeComponent();
}
```

ViewModel 通过事件关闭窗口：

```csharp
viewModel.RequestClose?.Invoke(true);
```

## 基类与命令

| 类 | 位置 | 用途 |
|-------|----------|---------|
| `ViewModelBase` | `UI/ViewModels/` | 抽象基类: `SetProperty<T>(ref T, T, [CallerMemberName])`, `OnPropertyChanged()` |
| `RelayCommand` | `UI/Commands/` | 同步操作 `ICommand`，`Func<bool>?` canExecute |
| `AsyncRelayCommand` | `UI/Commands/` | 异步操作 `ICommand`，`isExecuting` 守卫，`RaiseCanExecuteChanged()` |

## MVVM 约定

- 每个 ViewModel 继承 `ViewModelBase`。
- 属性: `{ get => field; set => SetProperty(ref field, value); }`
- 命令: `RelayCommand` (同步) / `AsyncRelayCommand` (异步)。
- 异步初始化: 构造函数中 `_ = InitializeAsync();` 即发即忘。
- ViewModel 通过构造函数接收**所有依赖**。
- 命名: `{Feature}ViewModel`，位于 `StarPakExplorer.UI.ViewModels`。
