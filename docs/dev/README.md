# Weed 开发文档

本目录集中保存技术实现、插件 SDK、构建测试和发布指引。面向使用者的安装与功能说明位于[根 README](../../README.md)和[使用指南](../user-guide.md)。

## 开发环境

- Windows 10 或更高版本
- .NET 9 SDK
- PowerShell
- 可选：GitHub CLI，用于执行 Release 脚本

在仓库根目录运行：

```powershell
dotnet build Weed.sln
dotnet run --project Weed.SmokeTests\Weed.SmokeTests.csproj
dotnet run --project Weed.App\Weed.App.csproj
```

## 文档导航

- [产品边界与术语](00-overview.md)
- [系统架构](01-system-architecture.md)
- [插件系统](02-plugin-system.md)
- [查询路由与快捷键](03-query-routing-hotkeys.md)
- [界面与交互约定](04-ui-ux.md)
- [第一方插件规格](05-first-party-plugins.md)
- [数据与存储](06-data-storage.md)
- [路线图与验收项](07-roadmap.md)
- [外部插件开发与分发](08-external-plugins.md)

## 插件开发入口

1. 阅读[插件系统](02-plugin-system.md)了解 manifest、生命周期与 Host API。
2. 从 [`templates/plugin`](../../templates/plugin/README.md) 创建插件项目。
3. 按[外部插件开发与分发](08-external-plugins.md)发布并导入测试包。
4. 使用 [`schemas/manifest.schema.json`](../../schemas/manifest.schema.json)校验 manifest。

外部插件与 Host 共享进程。权限字段用于向用户说明所需能力，并不构成安全沙箱。

## 发布

日常发布由根目录脚本完成：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\release-github.ps1 `
  -Version 0.1.5 `
  -CommitMessage "Release v0.1.5"
```

脚本会构建、运行 SmokeTests、提交全部变更、推送当前分支、创建并推送版本标签，然后生成 `win-x64` 包与更新清单并发布 GitHub Release。发布前应同步更新 `CHANGELOG.md`，并确认工作区中的全部变更都应进入该版本。

只生成本地发布包：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1
```

输出位于 `artifacts/`。当前包依赖目标机器已安装 .NET 9 Desktop Runtime x64。

## 文档维护

- 用户可见功能、入口或设置变化时，更新根 README、使用指南与对应插件 README。
- 实现、数据结构、接口或构建流程变化时，只在本目录及模板文档中说明。
- 每次更新都应在 `CHANGELOG.md` 中记录；未发布内容放入 `Pre-release`，发布时归入对应版本。
- 示例版本号仅用于说明时使用 `0.1.0`；涉及当前发行版时必须与项目版本保持一致。
