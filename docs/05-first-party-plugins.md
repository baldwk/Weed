# First-party Plugins

## 概览

MVP 第一方插件：

- AppLauncher
- Calculator
- Clipboard
- Screenshot

第一方插件随 Weed 发布，默认启用，使用同一套 manifest、生命周期和结果模型。

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
