# Weed 内置插件指南

Weed 默认提供 8 个内置插件。你可以在 **Settings > Plugins** 中启用或禁用插件、调整无前缀搜索优先级，并修改插件专属设置。

| 插件 | 输入方式 | 用途 |
| --- | --- | --- |
| App Launcher | 直接输入应用名 | 搜索并启动应用 |
| Calculator | 直接输入表达式 | 计算并复制或粘贴结果 |
| Clipboard | `clip`、`Shift+Ctrl+C` | 搜索与使用剪切板历史 |
| Screenshot | `shot`、`Shift+Alt+A` | 区域、主屏与滚动截图 |
| Emoji Search | `emoji` | 搜索并复制 emoji |
| Translator | `tr`、`translate` | 快速翻译文本 |
| File Search | `file` | 通过 Everything 搜索文件 |
| Run Command | 直接输入命令名 | 打开常用 Windows 工具 |

## App Launcher

直接输入应用名称即可搜索开始菜单和 Windows 打包应用。搜索支持名称、拼音、拼音首字母与常见英文缩写。

```text
Visual Studio Code
weixin
wx
```

可用操作包括打开应用、以管理员身份运行、打开所在位置，以及复制路径或应用 ID。输入 `refresh apps` 可手动刷新应用列表。

在插件设置中可选择是否隐藏卸载程序和维护工具等低频快捷方式。

## Calculator

直接输入数学表达式即可看到结果，无需关键词。

```text
1+2*3
(3+5)/2
sqrt(9)
5!
50%
ln(e)
log(100)
log2(8)
```

支持：

- `+`、`-`、`*`、`/`、`%`、`^`、`**` 与括号。
- 阶乘和后缀百分号。
- `sqrt`、`abs`、`sin`、`cos`、`tan`、`round`。
- 自然对数 `ln`、常用对数 `log`，以及 `logN` 形式的任意底数对数。
- 常量 `pi` 和 `e`。
- 全角数字、中文括号、`×` 和 `÷` 等常见输入形式。

默认操作为复制结果；还可直接粘贴到前台窗口，或复制完整算式与结果。小数精度可在插件设置中调整。

## Clipboard

Clipboard 会在启用后记录剪切板历史。输入 `clip` 浏览最近内容，继续输入关键词即可搜索。

```text
clip meeting
clip type:text
clip type:image
clip type:files project
```

支持文本、图片、文件列表、HTML 和 RTF。搜索支持普通关键词、拼音、拼音首字母和类型筛选。

常用操作：

- 复制历史项，或直接粘贴到前台窗口。
- 打开图片、文件或富文本的预览与所在位置。
- 置顶常用内容。
- 删除不再需要的记录。

可在插件设置中控制是否记录图片与文件列表、保留天数、最大记录数、返回结果数和对象存储上限。剪切板内容保存在本机；退出 Weed 不会自动清空历史。

## Screenshot

输入 `shot`，或按 `Shift+Alt+A` 开始区域截图。

截图模式：

- **Capture region**：拖动选择区域。
- **Capture primary screen**：截取主屏幕。
- **Capture scrolling area**：选择滚动区域并拼接成长图。

区域截图后可使用画笔、矩形、椭圆、颜色、线宽、撤销、重做和清除等标注工具，然后复制到剪切板或保存为 PNG/JPEG。

插件设置可调整默认保存目录、格式、JPEG 质量、最大文件大小、标注颜色和线宽。

## Emoji Search

输入 `emoji` 后按英文名称、别名、分类或 shortcode 搜索。

```text
emoji smile
emoji rocket
emoji :heart:
```

默认操作为复制 emoji 字符，也可以复制 `:shortcode:` 或英文名称。插件设置可修改最大结果数和默认复制格式。

## Translator

使用 `tr` 或 `translate` 翻译文本。

```text
tr hello
tr en zh-CN hello
translate auto en 你好
translate ja zh-CN ありがとう
```

- `tr text`：使用插件中设置的默认源语言和目标语言。
- `tr source target text`：临时指定源语言与目标语言。

支持 Google Translate 与百度翻译。Google 可直接使用；百度翻译需要配置 App ID 和 Secret Key。默认操作为复制译文，也可以复制原文与译文，或交换语言后重新查询。

插件设置还可配置默认语言、第二目标语言、查询延迟、服务地址以及系统/无/自定义代理。翻译内容会发送到所选服务，请勿提交不希望第三方服务处理的敏感文本。

## File Search

File Search 使用 Everything 的本地索引。使用前请先安装 [Everything](https://www.voidtools.com/)。

```text
file report
file *.pdf invoice
file path:projects weed
```

查询支持 Everything 搜索语法。可用操作包括打开文件或文件夹、打开所在位置和复制完整路径。

插件设置可控制是否显示文件夹、最大结果数和排序方式。Weed 会在需要时尝试启动已经安装的 Everything，但不会安装 Everything，也不会修改它的开机启动设置。

## Run Command

直接输入常见 Windows 命令或工具名称。该插件只打开内置列表中的命令，不会执行任意输入。

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

输入完整命令通常排在最前，也可以按显示名称进行搜索。按 `Enter` 打开所选工具。

## 相关文档

- [Weed 使用指南](../docs/user-guide.md)
- [外部 OCR 插件](../External%20Plugins/Weed.Plugins.Ocr/README.md)
- [第一方插件技术规格](../docs/dev/05-first-party-plugins.md)
