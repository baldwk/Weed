# Weed 插件模板

本模板用于创建外部 managed .NET 插件。开始前请先阅读[插件系统](../../docs/dev/02-plugin-system.md)和[外部插件开发与分发](../../docs/dev/08-external-plugins.md)。

## 创建项目

1. 复制本目录到独立插件仓库。
2. 重命名项目、命名空间和入口类。
3. 同步修改 `manifest.json` 中的 `id`、`name`、`version`、`assembly` 与 `entryType`。
4. 更新 `Example.Plugin.csproj` 中 `Weed.Abstractions` 的引用路径。
5. 实现 `IWeedPlugin`，并按需实现查询、命令、设置或常驻生命周期接口。

插件 ID 应保持稳定并使用反向域名或明确命名空间，例如 `com.example.weather`。发布新版本时同时更新项目版本与 manifest 版本。

## 发布

不要只复制 `dotnet build` 生成的 DLL。请发布完整目录：

```powershell
dotnet publish .\Example.Plugin.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o .\dist\example.plugin
```

确保发布目录根部包含：

- `manifest.json`
- 插件 DLL
- `.deps.json`
- 所有依赖 DLL、原生库和运行资源

将发布目录中的内容直接压缩，确保 ZIP 根部可以看到 `manifest.json`，不要再多包一层父目录。

## 本地验证

1. 在 Weed 的 **Settings > External Plugins** 中导入 ZIP、发布目录或源码目录。
2. 重启 Weed。
3. 检查插件是否启用，并验证查询、默认操作、其他动作与设置。
4. 在插件详情中检查 manifest、权限声明和日志。
5. 从干净目录重新解压最终 ZIP，再做一次导入验证。

外部插件会在 Weed 进程内运行。manifest 中的权限是面向用户的能力声明，不是安全沙箱；请准确、最小化地声明所需权限，并在插件 README 中说明网络请求与数据处理方式。

## 分发

建议在插件自己的 GitHub 仓库中通过 Release 发布版本化 ZIP，并提供变更说明与 SHA256。完整的仓库布局、打包命令、导入规则和故障排查见[外部插件开发与分发](../../docs/dev/08-external-plugins.md)。
