# 发布流程

- **工作流文件**: `.github/workflows/release-on-tag.yml`
- **触发条件**: 仅推送匹配 `v*` 的标签
- **构建**: `dotnet publish` 自包含 win-x64 单文件
- **输出**: zip 压缩包上传至 GitHub Release 资源
- **本地构建产物**: 通过 `artifacts/` 在 `.gitignore` 中忽略

```powershell
# 本地发布测试
dotnet publish .\StarPakExplorer.UI\StarPakExplorer.UI.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```
