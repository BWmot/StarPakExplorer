# Release Workflow

- **Workflow file**: `.github/workflows/release-on-tag.yml`
- **Trigger**: push tags matching `v*` only
- **Build**: `dotnet publish` self-contained win-x64 single-file
- **Output**: zip archive uploaded to GitHub Release assets
- **Local artifacts**: ignored via `artifacts/` in `.gitignore`

```powershell
# Publish locally for testing
dotnet publish .\StarPakExplorer.UI\StarPakExplorer.UI.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```
