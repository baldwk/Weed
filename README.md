# Weed

Weed 是一款面向 Windows 的快捷启动器与效率工具。按下 `Alt+Space`，即可在一个统一入口中启动应用、计算表达式、搜索剪切板、翻译文本、查找文件或发起截图。

## 功能亮点

- **应用启动**：按名称、拼音、拼音首字母或英文缩写查找应用，并支持管理员运行、打开位置和复制路径。
- **即时计算**：直接输入数学表达式，支持常用运算、函数、常量、阶乘、百分比与不同底数的对数。
- **剪切板历史**：保存并搜索文本、图片、文件列表、HTML 和 RTF，支持置顶、删除、复制与直接粘贴。
- **截图与标注**：支持区域截图、主屏截图和滚动长截图，并可使用画笔、矩形、椭圆等工具标注。
- **快捷翻译**：通过 Google Translate 或百度翻译快速翻译文本，可设置默认语言与代理。
- **文件搜索**：调用 Everything 的本地索引，快速查找文件和文件夹。
- **Emoji 搜索**：按名称、别名、分类或 shortcode 搜索并复制 emoji。
- **系统命令**：快速打开任务管理器、注册表、服务、控制面板等常用 Windows 工具。
- **可扩展插件**：支持从 ZIP、DLL、已编译目录或源码目录导入外部插件。

## 界面预览

| 应用启动 | 计算器 |
| --- | --- |
| <img src="screenshots/01-app-launcher.png" alt="应用启动搜索结果" width="420"> | <img src="screenshots/02-calculator.png" alt="计算器结果" width="420"> |
| 剪切板历史 | 翻译 |
| <img src="screenshots/03-clipboard-history.png" alt="剪切板历史与图片预览" width="420"> | <img src="screenshots/04-translator.png" alt="翻译结果" width="420"> |
| OCR | 截图操作 |
| <img src="screenshots/07-ocr-result.png" alt="OCR 识别结果" width="420"> | <img src="screenshots/08-screenshot-actions.png" alt="截图操作列表" width="420"> |
| 快捷键设置 | 插件设置 |
| <img src="screenshots/09-preferences-hotkeys.png" alt="快捷键设置" width="420"> | <img src="screenshots/10-plugin-settings.png" alt="插件设置" width="420"> |

## 安装

1. 前往 [Releases](https://github.com/baldwk/Weed/releases/latest) 下载 `Weed-win-x64.zip`。
2. 安装 [.NET 9 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/9.0)，如系统中已经安装可跳过。
3. 将压缩包完整解压到固定目录，然后运行 `Weed.App.exe`。

Weed 当前支持 Windows 10 及以上 64 位系统。首次运行后会显示启动器并创建托盘图标；重复启动会唤起已经运行的 Weed。

> Weed 尚未提供安装程序，也未进行代码签名。请从本仓库 Release 页面下载，并在解压后运行。

## 快速上手

按 `Alt+Space` 唤起 Weed，输入内容后使用方向键选择结果，按 `Enter` 执行默认操作。结果提供更多操作时，可按界面提示选择打开、复制、粘贴、定位或删除等动作。

| 输入 | 用途 |
| --- | --- |
| `vscode`、`wx` | 按名称、拼音或缩写启动应用 |
| `1+2*3`、`ln(e)`、`log2(8)` | 计算表达式 |
| `clip meeting` | 搜索剪切板历史 |
| `clip type:image` | 只查看剪切板图片 |
| `shot` | 打开截图操作 |
| `tr hello` | 使用默认语言设置翻译文本 |
| `tr en zh-CN hello` | 指定源语言和目标语言 |
| `emoji rocket` | 搜索 emoji |
| `file report.pdf` | 通过 Everything 搜索文件 |
| `taskmgr` | 打开任务管理器 |

默认快捷键：

- `Alt+Space`：唤起 Weed。
- `Shift+Ctrl+C`：打开剪切板历史。
- `Shift+Alt+A`：开始区域截图。
- `Shift+Alt+O`：安装 OCR 外部插件后，截图并识别文字。

快捷键、主题、开机启动、插件启用状态和插件参数均可在设置中修改。

## 插件

Weed 内置 App Launcher、Calculator、Clipboard、Screenshot、Emoji Search、Translator、File Search 和 Run Command。完整用法见[内置插件指南](Built-In%20Plugins/README.md)。

仓库中还包含可单独打包导入的 OCR 外部插件，使用方法见 [OCR 插件说明](External%20Plugins/Weed.Plugins.Ocr/README.md)。外部插件会在 Weed 进程内运行，请只导入来源可信的插件。

## 使用文档

- [使用指南](docs/user-guide.md)：日常操作、设置、更新、数据位置与故障排查。
- [内置插件指南](Built-In%20Plugins/README.md)：每个内置插件的入口、操作和设置。
- [文档索引](docs/README.md)：用户文档与开发文档总览。
- [开发文档](docs/dev/README.md)：构建、架构、插件 SDK 与发布流程。

## 许可证

本仓库目前未包含开源许可证。除非仓库后续明确添加许可证，否则默认保留所有权利。
