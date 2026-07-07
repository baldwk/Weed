# Query Routing And Hotkeys

## 查询入口

Weed 支持三种用户入口：

```text
Keyword
Hotkey
ImplicitQuery
```

## QueryContext

```csharp
public sealed record QueryContext
{
    public required string RawText { get; init; }
    public required string NormalizedText { get; init; }
    public required QueryActivation Activation { get; init; }
    public string? Keyword { get; init; }
    public string? Command { get; init; }
    public DateTimeOffset StartedAt { get; init; }
}
```

输入规范化包括：

- 去除首尾空白。
- 全角字符转半角。
- 中文标点转常用英文符号。
- 大小写折叠。
- 连续空白合并。

## Keyword 路由

Keyword 查询流程：

```text
User input
  -> Parse first token
  -> Match keyword registry
  -> Build QueryContext with keyword
  -> Dispatch to target plugin
  -> Render plugin results
```

示例：

```text
clip invoice
```

`clip` 命中剪切板插件后，`invoice` 作为查询内容传给插件。

## Hotkey 路由

Hotkey 流程：

```text
Global hotkey pressed
  -> HotkeyManager resolves command
  -> Core builds CommandContext
  -> PluginHost dispatches command
  -> Plugin returns CommandResult
  -> App shows panel, starts tool, or closes according to result
```

Hotkey 可触发三类行为：

| 行为 | 说明 | 示例 |
| --- | --- | --- |
| `showLauncher` | 打开 Weed 主窗口并进入指定模式。 | 打开插件搜索面板。 |
| `showPluginPanel` | 打开插件专用面板。 | 展示剪切板历史。 |
| `executeCommand` | 直接执行命令。 | 进入区域截屏。 |

## ImplicitQuery 路由

ImplicitQuery 是无前缀查询。Host 将输入分发给声明了 `implicitQuery` 的插件。

```text
User input
  -> Normalize
  -> Dispatch to implicit providers
  -> Collect results
  -> Apply ranking
  -> Render merged list
```

MVP 第一方 implicit 插件：

- AppLauncher
- Calculator

第三方插件声明 implicit 入口后，可在设置页配置插件优先级。

## 排序公式

ImplicitQuery 使用三项总分：

```text
FinalScore = PluginMatchScore + UsageScore + PriorityScore
```

| 项 | 范围 | 来源 | 说明 |
| --- | --- | --- | --- |
| `PluginMatchScore` | 0..30 | 插件 | 插件对当前查询和结果的匹配度判断。 |
| `UsageScore` | 0..30 | Weed | 用户历史选择行为。 |
| `PriorityScore` | 0..100 | 用户配置 | 插件级别优先级，默认 0。 |

Keyword 和 Hotkey 入口直接命中目标插件，插件优先级只参与 ImplicitQuery 合并排序。

## PluginMatchScore

插件返回每条结果的 `MatchScore`，范围为 `0..30`。

推荐解释：

| 分数 | 含义 |
| --- | --- |
| `0` | 当前结果不展示。 |
| `1..10` | 弱匹配。 |
| `11..20` | 普通匹配。 |
| `21..30` | 强匹配。 |

AppLauncher 示例：

- 完整名称前缀匹配：高分。
- 拼音首字母匹配：中高分。
- 模糊包含匹配：中分。
- 弱相似匹配：低分。

Calculator 示例：

- 明确表达式并能求值：高分。
- 需要补全或存在歧义：低到中分。

## UsageScore

Weed 根据用户选择行为计算历史分。历史记录键由以下字段组成：

```text
pluginId
resultId
defaultCommand
```

计分规则：

- 用户执行某个结果后，该结果的使用权重增加。
- 最近使用的结果获得更高权重。
- 同一查询下历史最高权重映射为 30 分，其余按比例映射。
- 没有历史记录的结果为 0 分。

## PriorityScore

插件优先级是用户配置项，范围 `0..100`，默认 `0`。

配置粒度：

```text
Plugin-level
```

只有支持 ImplicitQuery 的插件在设置页展示该项。用户配置后的值参与该插件所有 implicit 结果排序。

## Tie-breaker

总分相同时按以下顺序稳定排序：

```text
1. 最近被选择的结果在前
2. 插件返回顺序靠前的结果在前
3. 插件 ID 字典序
4. Result ID 字典序
```

## 查询取消和增量刷新

- 每次输入变化都会创建新的查询上下文。
- 旧查询的 `CancellationToken` 会被触发。
- 已返回的结果可以立即展示。
- 后续返回的结果通过同一排序规则合并。
- UI 线程只接收最终可渲染状态。

## Hotkey Manifest

```json
{
  "type": "hotkey",
  "command": "clipboard.show",
  "defaultKeys": "Shift+Ctrl+C",
  "configurable": true,
  "behavior": "showPluginPanel"
}
```

字段说明：

| 字段 | 说明 |
| --- | --- |
| `command` | 触发时分发给插件的命令 ID。 |
| `defaultKeys` | 插件提供的默认快捷键。 |
| `configurable` | 用户是否可以修改。 |
| `behavior` | Host 对命令结果的窗口处理方式。 |

## 快捷键配置

Host 统一管理全局快捷键：

- 启动时读取所有插件默认快捷键。
- 应用用户配置覆盖。
- 检查快捷键冲突。
- 注册有效快捷键。
- 插件禁用时注销对应快捷键。
- 插件更新后保留用户配置。

快捷键设置页提供：

- 当前按键。
- 修改按键。
- 禁用该快捷键。
- 恢复默认。
- 冲突提示。
- 所属插件和命令说明。

## 快捷键表示

快捷键使用规范化字符串：

```text
Ctrl+Shift+C
Alt+Space
Shift+Alt+A
```

修饰键顺序：

```text
Ctrl -> Shift -> Alt -> Win
```

用户输入快捷键时，UI 实时解析并显示规范化结果。
