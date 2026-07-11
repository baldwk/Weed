# Built-In Plugins 功能说明

本文档说明 Weed 随应用一起编译和发布的内置插件。内置插件源码位于 `Built-In Plugins/`，由 `Weed.App` 通过项目引用加载，并在启动时使用各插件代码里的静态 `Manifest` 注册到 `PluginRuntime`。它们不需要外部 `manifest.json` 包，也不会走外部插件安装流程。

## 插件总览

| 插件 | Plugin ID | 入口 | 主要用途 |
| --- | --- | --- | --- |
| App Launcher | `weed.appLauncher` | 无前缀输入应用名 | 搜索并启动开始菜单和 Windows 打包应用。 |
| Calculator | `weed.calculator` | 无前缀数学表达式 | 计算表达式并复制或粘贴结果。 |
| Clipboard | `weed.clipboard` | `clip`、`Shift+Ctrl+C` | 记录、搜索、复制、粘贴剪切板历史。 |
| Emoji Search | `weed.emoji` | `emoji` | 搜索内置 emoji 并复制 emoji、shortcode 或名称。 |
| Translator | `weed.translate` | `tr`、`translate` | 调用翻译服务快速翻译文本。 |
| File Search | `weed.fileSearch` | `file` | 通过 Everything 索引搜索本地文件和文件夹。 |
| Run Command | `weed.runCommand` | 无前缀系统命令名 | 快速打开常用 Windows 管理工具和系统程序。 |
| Screenshot | `weed.screenshot` | `shot`、`Shift+Alt+A` | 区域、主屏和滚动截图。 |

## App Launcher

App Launcher 是无前缀查询插件。用户直接输入应用名称、拼音、拼音首字母或缩写即可搜索应用。

功能：

- 索引当前用户和所有用户的开始菜单快捷方式。
- 索引 `shell:AppsFolder` 中的 Windows 打包应用。
- 默认过滤启动目录快捷方式、卸载程序和维护类快捷方式。
- 提取应用图标并写入插件缓存目录。
- 将应用索引保存到 `app-launcher.db`，启动时优先加载缓存。
- 支持精确匹配、前缀匹配、包含匹配、子序列匹配、拼音匹配、拼音首字母匹配和英文缩写匹配。
- 支持 `refresh apps`、`refresh applications`、`app refresh`、`apps refresh` 手动刷新索引。

动作：

- `Open`: 打开应用。
- `Run as administrator`: 以管理员身份打开传统桌面应用。Windows 打包应用不支持此动作。
- `Open location`: 打开应用目标所在目录。
- `Copy path` 或 `Copy app ID`: 复制目标路径或打包应用 ID。

设置：

- `hideMaintenanceShortcuts`: 是否隐藏卸载和维护类快捷方式，默认 `true`。

## Calculator

Calculator 是无前缀查询插件。输入看起来像数学表达式的内容时，插件返回一条计算结果。

示例：

```text
1+2*3
sqrt(9)
(3+5)/2
5!
2^8
50%
```

功能：

- 支持 `+`、`-`、`*`、`/`、`%`、`^` 和 `**`。
- 支持括号、一元正负号、阶乘和后缀百分号。
- 支持 `sqrt`、`abs`、`sin`、`cos`、`tan`、`round`。
- 支持常量 `pi` 和 `e`。
- 通过全局输入规范化支持全角数字、中文括号、`×`、`÷` 等常见输入形式。
- 结果按 `decimalPrecision` 配置值格式化，未配置时默认最多 10 位小数。

动作：

- `Copy result`: 复制计算结果。
- `Paste result`: 将计算结果粘贴到前台窗口。
- `Copy equation`: 复制完整表达式和结果。

## Clipboard

Clipboard 是常驻插件。启用后会启动原生剪切板监听，并使用定时轮询作为补充，捕获剪切板变化后写入本地数据库。

入口：

```text
clip keyword
clip type:image
clip type:files project
Shift+Ctrl+C
```

功能：

- 捕获纯文本、图片、文件列表、RTF 和 HTML。
- 图片、RTF、HTML 等大对象写入独立对象目录，元数据和可搜索文本写入 `clipboard.db`。
- 支持 SQLite FTS 搜索、拼音搜索、拼音首字母搜索、子序列匹配和 `type:` 类型过滤。
- 搜索结果优先展示置顶项，再按匹配度和时间排序。
- 支持重复内容去重、过期清理、最大记录数限制和对象空间配额。
- 支持针对图片、文件、HTML、RTF 打开预览或所在位置。

动作：

- `Copy`: 将历史项复制回剪切板。
- `Paste`: 复制历史项并粘贴到前台窗口。
- `Open preview`: 打开图片、文件、HTML 或 RTF 的预览/位置。
- `Pin` / `Unpin`: 置顶或取消置顶。
- `Delete`: 删除历史项。

设置：

- `captureImages`: 是否捕获图片，默认 `true`。
- `captureFileLists`: 是否捕获文件列表，默认 `true`。
- `retentionDays`: 记录保留天数，默认 `180`。
- `maxItems`: 最大记录数，默认 `100000`。
- `resultLimit`: 启动器返回的最大结果数，默认 `100`。
- `maxObjectMegabytes`: 大对象存储上限，默认 `2048` MB。

## Emoji Search

Emoji Search 使用内置 emoji 数据文件，支持按名称、分类、别名和 shortcode 搜索。

入口：

```text
emoji smile
emoji rocket
emoji :heart:
```

功能：

- 从 `emoji-test.txt` 加载 emoji 数据，缺失时使用内置回退数据。
- 支持名称匹配、shortcode 匹配、别名匹配、分类匹配和子分类匹配。
- 对常见词建立别名，例如 `love` 可匹配 heart 相关 emoji。
- 空查询时返回常用/靠前的 emoji 列表。

动作：

- `Copy emoji`: 复制 emoji 字符。
- `Copy shortcode`: 复制 `:shortcode:`。
- `Copy name`: 复制 emoji 名称。

设置：

- `maxResults`: 最大结果数，默认 `30`，范围 `5` 到 `100`。
- `copyFormat`: 回车默认复制格式，可选 `Emoji`、`Shortcode`、`Name`，默认 `Emoji`。

## Translator

Translator 是关键词查询插件，支持默认语言翻译和显式语言对翻译。

入口：

```text
tr hello
tr en zh-CN hello
translate auto en 你好
translate ja zh-CN ありがとう
```

功能：

- `tr text`: 使用默认源语言和默认目标语言，源语言默认 `auto`。
- `tr source target text`: 使用显式源语言和目标语言。
- 支持 Google Translate 和 Baidu Translate。
- Google 默认使用 `https://translate.googleapis.com`。
- Baidu 需要配置 app ID 和 secret key。
- 支持系统代理、无代理和自定义代理 URL。
- 支持查询延迟，避免每次输入变化都立即请求接口。
- 当未显式指定语言对，且检测到源语言已经是默认目标语言时，可自动改用 secondary target language，默认 `en`。
- 翻译失败时返回一条诊断结果，并记录插件日志。

动作：

- `Copy translation`: 复制译文。
- `Copy source and translation`: 复制原文和译文。
- `Swap languages`: 交换源语言和目标语言后重新打开查询。源语言为 `auto` 时不会交换。

设置：

- `provider`: 翻译 provider，默认 `google`。
- `defaultSourceLanguage`: 默认源语言，默认 `auto`。
- `defaultTargetLanguage`: 默认目标语言，默认 `zh-CN`。
- `secondaryTargetLanguage`: 二级目标语言，默认 `en`。
- `queryDelayMilliseconds`: 查询延迟，默认 `500` ms。
- `googleBaseUrl`: Google 翻译基础地址。
- `baiduAppId`: 百度翻译 app ID。
- `baiduSecretKey`: 百度翻译 secret key。
- `baiduBaseUrl`: 百度翻译接口地址。
- `proxyMode`: `system`、`none` 或 `custom`。
- `proxyUrl`: 自定义代理地址。

## File Search

File Search 通过 Everything SDK 查询 Everything 已有的本地索引。Weed 不自行递归扫描磁盘，也不建立自己的全盘文件索引。

Everything SDK DLL 会随 Weed 发布，不需要单独开机自启；但 Everything 后台程序必须运行并提供 IPC。启用 File Search 时，Weed 会在启动阶段检查 IPC，并在需要时尝试启动已安装的 Everything。Weed 不会修改 Everything 自身的开机启动配置。

入口：

```text
file report
file *.pdf invoice
file path:projects weed
```

功能：

- 直接把查询传给 Everything，支持 Everything 查询语法。
- 可选择是否包含文件夹；关闭后会在查询前追加 `file:`。
- 可选择 Everything SDK 排序方式，默认按名称升序。
- 结果包含完整路径，并按 Everything 返回顺序映射为 Weed 结果分数。
- 仓库随插件携带 `Everything64.dll` 和 `Everything32.dll`，运行时按进程位数选择。
- Everything 未安装、IPC 不可用、查询非法或 SDK 调用失败时返回诊断结果。

动作：

- `Open`: 打开文件或文件夹。
- `Open location`: 打开所在目录。
- `Copy path`: 复制完整路径。

设置：

- `includeFolders`: 是否包含文件夹结果，默认 `true`。
- `maxResults`: 最大结果数，默认 `50`，范围 `5` 到 `200`。
- `sort`: Everything SDK 排序方式，默认 `Name ascending`，可选名称、路径、大小、扩展名、类型、日期、属性、运行次数等升序/降序排序。

## Run Command

Run Command 是无前缀查询插件，用于快速启动内置 Windows 命令和管理工具。它只执行插件内置白名单里的命令，不执行任意用户输入。

支持命令：

```text
cmd
regedit
taskmgr
services.msc
devmgmt.msc
diskmgmt.msc
control
appwiz.cpl
ncpa.cpl
sysdm.cpl
mstsc
notepad
calc
explorer
```

功能：

- 按命令别名和显示名称匹配。
- 支持精确匹配、前缀匹配和包含匹配。
- 最多返回 8 条结果。

动作：

- `Open`: 通过 Shell 打开对应系统命令。

## Screenshot

Screenshot 支持通过关键词或全局热键发起截图。

入口：

```text
shot
Shift+Alt+A
```

功能：

- `Capture region`: 交互式区域截图。
- `Capture primary screen`: 截取主屏。
- `Capture scrolling area`: 选择可滚动区域，捕获多帧后拼接成长图。
- 截图结果可复制到剪切板，也可保存为图片文件。
- 标注工具的默认颜色、线宽、保存格式和 JPEG 质量由插件设置控制。

动作：

- `Capture region`: 选择区域并截图。
- `Capture primary screen`: 截取主屏。
- `Capture scrolling area`: 捕获滚动区域。

设置：

- `defaultSaveDirectory`: 默认保存目录。
- `defaultFormat`: 默认保存格式，`PNG` 或 `JPEG`，默认 `PNG`。
- `jpegQuality`: JPEG 质量，默认 `90`。
- `maxSavedFileMegabytes`: 最大保存文件大小，默认 `2` MB。
- `defaultColor`: 默认标注颜色，默认 `Red`。
- `defaultLineWidth`: 默认标注线宽，默认 `4`。
