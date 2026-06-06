using System.Collections.ObjectModel;

namespace StarPakExplorer.UI.ViewModels;

public sealed class PackTreeNodeViewModel : ViewModelBase
{
    private bool isChecked;
    private bool isExpanded;
    private bool suppressUpdates;

    public PackTreeNodeViewModel(
        string displayName,
        string fullPath,
        string relativePath,
        bool isFolder,
        PackTreeNodeViewModel? parent,
        Action? selectionChanged)
    {
        DisplayName = displayName;
        FullPath = fullPath;
        RelativePath = relativePath;
        IsFolder = isFolder;
        Parent = parent;
        this.selectionChanged = selectionChanged;
    }

    private readonly Action? selectionChanged;

    public string DisplayName { get; }

    public string FullPath { get; }

    public string RelativePath { get; }

    public bool IsFolder { get; }

    public PackTreeNodeViewModel? Parent { get; }

    public ObservableCollection<PackTreeNodeViewModel> Children { get; } = [];

    public bool IsChecked
    {
        get => isChecked;
        set
        {
            if (!SetProperty(ref isChecked, value))
            {
                return;
            }

            if (suppressUpdates)
            {
                return;
            }

            if (IsFolder)
            {
                SetChildrenChecked(value);
            }

            Parent?.SyncCheckedStateFromChildren();
            selectionChanged?.Invoke();
        }
    }

    public bool IsExpanded
    {
        get => isExpanded;
        set => SetProperty(ref isExpanded, value);
    }

    public string InfoText => IsFolder ? $"[{Children.Count}]" : string.Empty;

    public void AddChild(PackTreeNodeViewModel child)
    {
        Children.Add(child);
        OnPropertyChanged(nameof(InfoText));
    }

    public IEnumerable<string> GetCheckedFilePaths()
    {
        if (!IsFolder)
        {
            if (IsChecked)
            {
                yield return RelativePath;
            }

            yield break;
        }

        foreach (var child in Children)
        {
            foreach (var path in child.GetCheckedFilePaths())
            {
                yield return path;
            }
        }
    }

    public int GetCheckedFileCount()
    {
        return GetCheckedFilePaths().Count();
    }

    public void MarkExpandedRecursive(bool expanded)
    {
        IsExpanded = expanded;
        foreach (var child in Children)
        {
            child.MarkExpandedRecursive(expanded);
        }
    }

    private void SetChildrenChecked(bool value)
    {
        suppressUpdates = true;
        try
        {
            foreach (var child in Children)
            {
                child.IsChecked = value;
            }
        }
        finally
        {
            suppressUpdates = false;
        }
    }

    private void SyncCheckedStateFromChildren()
    {
        if (!IsFolder || Children.Count == 0)
        {
            return;
        }

        var shouldBeChecked = Children.All(child => child.IsChecked);
        if (shouldBeChecked == IsChecked)
        {
            return;
        }

        suppressUpdates = true;
        try
        {
            isChecked = shouldBeChecked;
            OnPropertyChanged(nameof(IsChecked));
        }
        finally
        {
            suppressUpdates = false;
        }

        Parent?.SyncCheckedStateFromChildren();
    }
}
