# Weed Overview

## 产品定位

Weed 是 Windows 10+ 上的 Alfred 风格启动器和效率工具。它以一个高性能搜索入口为核心，通过 managed DLL 插件扩展能力，覆盖程序搜索、计算器、剪切板历史、截屏和后续工作流。

Weed 的第一目标是日常使用速度和审美一致性。插件可以提供功能和结果，但结果展示、键盘交互、主题、快捷键配置和排序体验由 Host 统一管理。

## MVP 范围

MVP 包含以下能力：

- `Alt+Space` 唤起主搜索窗口。
- Alfred 风格搜索 UI、结果列表、键盘导航和基础设置。
- managed DLL 插件运行时。
- 三种插件入口：`Keyword`、`Hotkey`、`ImplicitQuery`。
- 用户可配置全局快捷键。
- 用户可配置插件优先级，优先级参与无前缀查询排序。
- 第一方插件：AppLauncher、Calculator、Clipboard、Screenshot。
- 插件开发基础文档、manifest schema 和 C# 插件模板。

## 核心原则

- Host 保持轻量，负责体验和调度。
- 功能能力优先通过插件实现。
- 第一方插件和第三方插件使用同一套公开抽象。
- 插件入口使用 managed .NET DLL。
- UI 由 Weed 统一渲染，保证风格一致。
- 用户配置拥有明确权重，尤其是插件优先级和快捷键。
- 查询、索引和插件调用采用异步模型，输入体验保持流畅。
- Windows 10 是最低支持平台。

## 技术栈

- 语言和运行时：C#、.NET 10 LTS。
- 桌面 UI：WPF。
- 本地数据：SQLite、FTS5。
- 插件入口：managed .NET DLL。
- 平台能力：Win32 API、Windows Shell API、WPF interop。

## 关键术语

- **Host**: Weed 主程序，负责窗口、主题、设置、快捷键、插件生命周期、查询路由和结果排序。
- **Plugin**: 插件目录中的 managed DLL，提供查询、命令、常驻服务或平台功能。
- **First-party plugin**: 随 Weed 发布的官方插件，默认启用，仍遵循插件协议。
- **Keyword**: 带缩写或命令前缀的查询入口，例如 `clip hello`。
- **Hotkey**: 用户可配置的全局快捷键入口，例如 `Shift+Ctrl+C`。
- **ImplicitQuery**: 无前缀查询入口，例如输入 `1+2` 或 `vscode`。
- **Resident plugin**: 启用后需要保持运行的插件，例如剪切板历史插件。
- **Result**: 插件返回给 Host 的结构化搜索结果。
- **Action**: 用户对 Result 执行的操作，例如打开、复制、粘贴、删除或保存。
