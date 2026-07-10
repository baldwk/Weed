# First-party Plugins

## 概览

MVP 第一方插件：

- AppLauncher
- Calculator
- Clipboard
- Screenshot

第一方插件随 Weed 发布，默认启用，使用同一套 manifest、生命周期和结果模型。

当前仓库还包含以下第一方扩展插件：

- Emoji Search
- Translator
- File Search

## AppLauncher

### 入口

```json
{
  "type": "implicitQuery",
  "provider": "appLauncher"
}
```

### 范围

MVP 索引 Windows 开始菜单中的应用快捷方式。

索引路径：

```text
%ProgramData%\Microsoft\Windows\Start Menu\Programs
%AppData%\Microsoft\Windows\Start Menu\Programs
```

### 数据模型

```csharp
public sealed record AppEntry
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? TargetPath { get; init; }
    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? IconPath { get; init; }
    public required string ShortcutPath { get; init; }
    public DateTimeOffset IndexedAt { get; init; }
}
```

### 匹配

AppLauncher 支持：

- 英文名称匹配。
- 中文名称匹配。
- 拼音匹配。
- 拼音首字母匹配。
- 缩写匹配。
- 模糊匹配。

### 动作

- 打开应用。
- 以管理员身份打开。
- 打开所在位置。
- 复制路径。

默认动作是打开应用。

### 索引策略

- 启动时加载缓存。
- 首次使用时刷新开始菜单索引。
- 设置页提供手动刷新。
- 快捷方式变更后的刷新策略在后续迭代中扩展。

## Calculator

### 入口

```json
{
  "type": "implicitQuery",
  "provider": "calculator"
}
```

### 识别

Calculator 识别明确的数学表达式。

示例：

```text
1+2
12 * 8
sqrt(9)
(3+5)/2
```

输入规范化：

- 全角数字转半角。
- 中文括号转英文括号。
- `×` 转 `*`。
- `÷` 转 `/`。
- 中文空格合并。

### 结果

结果展示：

```text
1 + 2 * 3 = 7
```

默认动作：

- 复制结果。

可选动作：

- 粘贴结果到前台窗口。
- 复制完整表达式和结果。

### 计算能力

MVP 支持：

- 加减乘除。
- 括号。
- 幂运算。
- 百分号。
- 常用函数：`sqrt`、`abs`、`sin`、`cos`、`tan`、`round`。
- 常量：`pi`、`e`。

计算逻辑通过 `ICalculatorEngine` 适配器封装，便于替换表达式引擎。

## Clipboard

### Manifest

```json
{
  "id": "weed.clipboard",
  "runtime": {
    "resident": true
  },
  "activations": [
    {
      "type": "keyword",
      "keyword": "clip",
      "command": "clipboard.search"
    },
    {
      "type": "hotkey",
      "command": "clipboard.show",
      "defaultKeys": "Shift+Ctrl+C",
      "configurable": true,
      "behavior": "showPluginPanel"
    }
  ],
  "permissions": [
    "clipboard.read",
    "clipboard.write",
    "storage.local",
    "window.paste"
  ]
}
```

### 生命周期

Clipboard 是 resident 插件。启用后插件创建隐藏窗口并注册剪切板变化监听。

核心流程：

```text
StartAsync
  -> Create hidden HWND
  -> Register clipboard format listener
  -> Receive clipboard update message
  -> Queue async read
  -> Normalize and hash content
  -> Store metadata and searchable text
  -> Store large objects separately
```

### 支持内容

- 纯文本。
- 图片。
- 文件列表。
- RTF 摘要。
- HTML 摘要。

### 查询

入口：

```text
clip keyword
Shift+Ctrl+C
```

搜索能力：

- 全文搜索。
- 拼音搜索。
- 模糊搜索。
- 类型过滤。
- 最近记录排序。
- 置顶记录排序。

### 动作

- 复制到剪切板。
- 粘贴到前台窗口。
- 删除记录。
- 置顶或取消置顶。
- 打开图片预览。
- 打开文件所在位置。

### 保留策略

默认保留：

```text
100,000 条记录或 180 天
```

达到任一条件后执行清理。图片和大对象使用独立空间配额，用户可在设置页调整。

## Screenshot

### Manifest

```json
{
  "id": "weed.screenshot",
  "activations": [
    {
      "type": "hotkey",
      "command": "screenshot.region",
      "defaultKeys": "Shift+Alt+A",
      "configurable": true,
      "behavior": "executeCommand"
    },
    {
      "type": "keyword",
      "keyword": "shot",
      "command": "screenshot.open"
    }
  ],
  "permissions": [
    "screen.capture",
    "clipboard.write",
    "file.write"
  ]
}
```

### 区域截图

流程：

```text
Hotkey
  -> Show overlay
  -> Select region
  -> Capture bitmap
  -> Open editor
  -> Annotate
  -> Copy or save
```

区域选择 UI：

- 多显示器覆盖。
- 十字光标。
- 选择框。
- 像素尺寸提示。
- 放大镜辅助。

### 标注

编辑器支持：

- 画笔。
- 矩形框。
- 圆形或椭圆。
- 颜色选择。
- 线宽选择。
- 撤销。
- 重做。
- 清空标注。

输出动作：

- 复制图片。
- 保存图片。
- 另存为。
- 返回重新选择区域。

### 滚动截图

滚动截图通过截图插件 UI 进入。用户选择目标窗口或滚动区域后，Weed 捕获多个滚动位置的画面并拼接为长图。

流程：

```text
Open screenshot tool
  -> Choose scrolling capture
  -> Select target window or region
  -> Start capture
  -> Scroll and capture frames
  -> Stitch bitmap
  -> Open editor
  -> Annotate
  -> Copy or save
```

滚动截图 UI：

- 目标窗口高亮。
- 捕获区域确认。
- 进度展示。
- 停止按钮。
- 拼接预览。
- 编辑器入口。

保存格式：

- PNG。
- JPEG。

默认保存格式为 PNG。

## Emoji Search

Emoji Search 用于快速搜索并复制内置 emoji。

### Manifest

```json
{
  "id": "weed.emoji",
  "activations": [
    {
      "type": "keyword",
      "keyword": "emoji",
      "command": "emoji.search"
    }
  ],
  "permissions": [
    "clipboard.write"
  ]
}
```

### 查询

入口：

```text
emoji smile
emoji rocket
emoji heart
```

搜索能力：

- 名称匹配。
- 别名匹配。
- 分类匹配。
- shortcode 匹配。
- 常用 emoji 排序。

### 动作

- 复制 emoji。
- 复制 shortcode。
- 复制 emoji 名称。

默认动作是复制 emoji 到剪切板。

## Translator

Translator 用于通过免费或免费额度翻译接口进行快速翻译。

### Manifest

```json
{
  "id": "weed.translate",
  "activations": [
    {
      "type": "keyword",
      "keyword": "tr",
      "command": "translate.search"
    },
    {
      "type": "keyword",
      "keyword": "translate",
      "command": "translate.search"
    }
  ],
  "permissions": [
    "network",
    "clipboard.write"
  ]
}
```

### 查询

入口：

```text
tr hello
tr en zh hello
translate auto en 你好
translate ja zh ありがとう
```

语法：

- `tr text`: 使用默认源语言和目标语言，源语言默认自动检测。
- `tr source target text`: 指定源语言和目标语言。
- `translate source target text`: 完整命令别名。

### Provider

插件优先支持免费或免费额度 provider：

- Google Translate。
- Baidu Translate（百度翻译）。

Provider 应可在设置页切换。需要凭据的 provider 通过插件设置保存 API 配置；无需凭据或用户自配端点的 provider 也应支持 base URL 配置。

### 网络和代理

Translator 支持代理配置：

- `system`: 使用系统代理。
- `none`: 不使用代理。
- `custom`: 使用用户配置的代理地址。

请求失败、接口限额、provider 不可用或代理错误时，结果列表展示可执行的错误结果，并在插件日志中记录原因。

### 动作

- 复制译文。
- 复制原文和译文。
- 交换源语言和目标语言后重新翻译。

默认动作是复制译文到剪切板。

## File Search

File Search 用于通过 Everything 的本地索引快速搜索文件和文件夹。Weed 不自行递归扫描文件系统。

### Manifest

```json
{
  "id": "weed.fileSearch",
  "activations": [
    {
      "type": "keyword",
      "keyword": "file",
      "command": "file.search"
    }
  ],
  "permissions": [
    "file.read",
    "shell.launch"
  ]
}
```

### 依赖

File Search 依赖 Everything 已安装并正在运行。插件通过 Everything SDK 查询现有索引，不建立 Weed 自有文件索引，也不要求用户输入 Everything 可执行文件路径。

当 Everything 未安装、服务未运行或 API 不可用时，插件返回诊断结果，引导用户打开 Everything 或查看设置。

### 查询

入口：

```text
file report
file *.pdf invoice
file path:projects weed
```

搜索能力：

- 文件名搜索。
- 文件夹搜索。
- Everything 查询语法透传。
- 文件和文件夹类型过滤。
- Everything SDK 排序设置，包括名称、路径、大小、扩展名、日期、属性、运行次数等升序/降序排序。

### 动作

- 打开文件或文件夹。
- 打开所在位置。
- 复制路径。

默认动作是打开选中项。
