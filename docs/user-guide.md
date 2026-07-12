# Weed 使用指南

## 安装与启动

1. 从 [GitHub Releases](https://github.com/baldwk/Weed/releases/latest) 下载 `Weed-win-x64.zip`。
2. 确认系统已安装 [.NET 9 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/9.0)。
3. 将压缩包完整解压到固定目录，运行 `Weed.App.exe`。

首次启动会显示搜索窗口，并在通知区域创建托盘图标。关闭搜索窗口不会退出 Weed；需要退出时，请使用托盘菜单。再次运行 `Weed.App.exe` 会唤起现有窗口，而不会启动第二个进程。

## 搜索与操作

默认按 `Alt+Space` 唤起 Weed。输入关键词后：

- 使用 `↑` 和 `↓` 选择结果。
- 按 `Enter` 执行当前结果的默认操作。
- 按界面中的操作提示选择其他动作。
- 按 `Esc` 关闭搜索窗口。

应用、计算器和系统命令无需前缀；剪切板、截图、翻译、Emoji 与文件搜索使用各自的关键词。

| 场景 | 示例 |
| --- | --- |
| 启动应用 | `visual studio code`、`vscode`、应用名拼音首字母 |
| 计算 | `(3+5)/2`、`sqrt(9)`、`log10(100)` |
| 剪切板 | `clip invoice`、`clip type:files` |
| 截图 | `shot` |
| 翻译 | `tr hello`、`tr en zh-CN hello` |
| Emoji | `emoji smile`、`emoji :heart:` |
| 文件 | `file *.pdf invoice` |
| Windows 工具 | `taskmgr`、`services.msc` |

插件的完整查询语法与可用操作见[内置插件指南](../Built-In%20Plugins/README.md)。

## 设置

从搜索窗口右上角打开设置。常用项目包括：

- **General**：托盘图标、失去焦点时关闭窗口、开机启动。
- **Appearance**：跟随系统、浅色或深色主题。
- **Hotkeys**：修改主唤起快捷键及插件快捷键；快捷键冲突时请换用其他组合。
- **Plugins**：启用或禁用插件，并调整无前缀搜索中的插件优先级。
- **插件详情**：修改插件专属参数，查看声明信息与最近日志。
- **External Plugins**：导入 ZIP、DLL、目录或源码形式的插件。
- **Updates**：配置更新清单地址并检查更新。

设置会自动保存。更换外部插件或部分运行参数后，重启 Weed 可确保全部变更生效。

## 文件搜索准备

File Search 依赖 [Everything](https://www.voidtools.com/) 提供本地文件索引。请先安装 Everything；启用 File Search 后，Weed 会在需要时尝试启动已经安装的 Everything，但不会替你安装或修改其开机启动设置。

## 外部插件

打开 **Settings > External Plugins**，选择插件的 ZIP、DLL、已编译目录或源码目录进行导入。导入完成后重启 Weed。

外部插件不是沙箱应用，它与 Weed 在同一进程中运行，并可能按声明访问屏幕、剪切板、文件或网络。只安装来源可信、版本明确的插件。

OCR 插件需要额外的模型文件和运行依赖，详情见 [OCR 插件说明](../External%20Plugins/Weed.Plugins.Ocr/README.md)。

## 更新

新版本会发布在 [GitHub Releases](https://github.com/baldwk/Weed/releases)。可直接下载新版压缩包覆盖应用文件；覆盖或删除应用目录不会清除用户设置和历史数据。

也可以在 **Settings > Updates** 中填写 Release 提供的 `Weed-win-x64.update.json` 地址进行检查。Weed 会校验下载包的 SHA256，下载完成后仍需按界面提示完成更新。

## 数据位置

| 内容 | 位置 |
| --- | --- |
| 用户设置与快捷键 | `%APPDATA%\Weed` |
| 历史、缓存、插件数据与更新包 | `%LOCALAPPDATA%\Weed` |
| 外部插件 | `%LOCALAPPDATA%\Weed\plugins` |
| 日志 | `%LOCALAPPDATA%\Weed\logs` |
| 默认截图目录 | `%USERPROFILE%\Pictures\Weed` |

Weed 的搜索、历史与配置默认保存在本机。Translator 会将待翻译文本发送给你选择的翻译服务；导入的外部插件可能有自己的数据处理方式。

## 常见问题

### `Alt+Space` 没有响应

检查其他软件是否占用了该快捷键，然后从托盘打开设置并修改主快捷键。修改后若仍无效，请重启 Weed 并查看日志。

### 文件搜索提示 Everything 不可用

确认 Everything 已安装并正在运行，且其 IPC 可用。随后重新查询或重启 Weed。

### 外部插件导入后没有出现

重启 Weed，在插件详情中检查启用状态和日志。ZIP 包应保持原始目录结构，不要只提取其中一个 DLL 再导入。

### 如何彻底清除个人数据

退出 Weed 后删除 `%APPDATA%\Weed` 与 `%LOCALAPPDATA%\Weed`。此操作会永久删除设置、历史、缓存、截图插件数据和已导入插件，请先备份需要保留的内容。
