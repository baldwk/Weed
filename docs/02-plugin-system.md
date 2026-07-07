# Plugin System

## 插件类型

MVP 插件入口是 managed .NET DLL。插件通过 `Weed.Abstractions` 中的接口与 Host 通信。

推荐目标框架：

- `Weed.Abstractions`: `net10.0`
- Weed Host 和第一方 Windows 插件：`net10.0-windows`
- 第三方插件：根据是否使用 Windows API 选择 `net10.0` 或 `net10.0-windows`

插件入口类型必须是 public class，并实现 `IWeedPlugin`。插件可携带托管依赖和运行时资源。

## 插件目录结构

```text
plugins/
  weed.clipboard/
    manifest.json
    Weed.Plugins.Clipboard.dll
    assets/
      icon.png
    runtimes/
      win-x64/
        native/
```

`manifest.json` 是插件发现和加载的唯一入口。Host 根据 manifest 找到 DLL 和入口类型。

## Manifest

示例：

```json
{
  "id": "weed.clipboard",
  "name": "Clipboard History",
  "version": "0.1.0",
  "sdkVersion": "0.1",
  "assembly": "Weed.Plugins.Clipboard.dll",
  "entryType": "Weed.Plugins.Clipboard.ClipboardPlugin",
  "icon": "assets/icon.png",
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
      "configurable": true
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

## Manifest 字段

| 字段 | 说明 |
| --- | --- |
| `id` | 全局唯一插件 ID，使用反向域名或 `weed.*` 命名。 |
| `name` | 展示名称。 |
| `version` | 插件版本，使用 SemVer。 |
| `sdkVersion` | 插件编译时面向的 Weed SDK 主版本。 |
| `assembly` | 插件入口 DLL 文件。 |
| `entryType` | 实现 `IWeedPlugin` 的 public type 全名。 |
| `icon` | 插件图标路径。 |
| `runtime.resident` | 插件启用后是否需要常驻生命周期。 |
| `activations` | Keyword、Hotkey、ImplicitQuery 入口声明。 |
| `permissions` | 插件请求的 Host 能力。 |

## Activation

### Keyword

Keyword 使用固定前缀触发。Host 解析前缀后只把查询发送给对应插件。

```json
{
  "type": "keyword",
  "keyword": "clip",
  "command": "clipboard.search"
}
```

输入示例：

```text
clip meeting
```

### Hotkey

Hotkey 由插件声明默认值，Host 统一注册，用户可在设置中修改。

```json
{
  "type": "hotkey",
  "command": "screenshot.region",
  "defaultKeys": "Shift+Alt+A",
  "configurable": true
}
```

### ImplicitQuery

ImplicitQuery 用于无前缀查询。插件声明自己可接收无前缀查询，Host 将用户输入分发给这些插件并统一排序。

```json
{
  "type": "implicitQuery",
  "provider": "appLauncher"
}
```

## 生命周期接口

```csharp
public interface IWeedPlugin
{
    ValueTask InitializeAsync(IWeedHost host, CancellationToken ct);
    ValueTask DisposeAsync();
}
```

查询插件实现：

```csharp
public interface IQueryProvider
{
    string ProviderId { get; }
    ValueTask<IReadOnlyList<WeedResult>> QueryAsync(
        QueryContext context,
        CancellationToken ct);
}
```

命令插件实现：

```csharp
public interface ICommandHandler
{
    ValueTask<CommandResult> ExecuteAsync(
        CommandContext context,
        CancellationToken ct);
}
```

常驻插件实现：

```csharp
public interface IResidentPlugin
{
    ValueTask StartAsync(CancellationToken ct);
    ValueTask StopAsync(CancellationToken ct);
}
```

插件可以同时实现多个接口。Host 在初始化后读取插件暴露的 Provider 和 CommandHandler。

## Host API

`IWeedHost` 为插件提供受控服务：

```csharp
public interface IWeedHost
{
    IWeedLogger Logger { get; }
    IWeedSettings Settings { get; }
    IWeedStorage Storage { get; }
    IWeedClipboard Clipboard { get; }
    IWeedShell Shell { get; }
    IWeedWindowService Windows { get; }
    IWeedScreenCapture ScreenCapture { get; }
}
```

服务边界：

- `Logger`: 插件日志。
- `Settings`: 读取和保存插件配置。
- `Storage`: 插件私有数据目录和数据库路径。
- `Clipboard`: 剪切板读写辅助。
- `Shell`: 打开程序、文件、URL。
- `Windows`: 面板显示、粘贴、窗口定位。
- `ScreenCapture`: 屏幕捕获和显示器信息。

插件返回结构化结果，由 Weed App 负责渲染。

## WeedResult

```csharp
public sealed record WeedResult
{
    public required string Id { get; init; }
    public required string PluginId { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public WeedIcon? Icon { get; init; }
    public double MatchScore { get; init; } // 0..30
    public required string DefaultCommand { get; init; }
    public IReadOnlyList<WeedAction> Actions { get; init; } = [];
}
```

`Id` 需要在插件内稳定，便于 Host 记录使用历史。

## 权限

权限用于设置页展示、插件审核和 Host 服务访问控制。

常用权限：

```text
storage.local
clipboard.read
clipboard.write
window.paste
shell.launch
screen.capture
file.read
file.write
network
```

插件调用 Host 服务时，Host 根据 manifest 权限决定是否允许。

## AssemblyLoadContext

- 每个插件使用独立 `AssemblyLoadContext`。
- `Weed.Abstractions` 由 Host 默认上下文提供，插件包引用时设置为不复制到输出目录。
- 插件依赖优先从插件目录解析。
- 插件停止时先调用 `StopAsync`，再调用 `DisposeAsync`，最后释放加载上下文。
- 插件应释放事件订阅、Win32 句柄、数据库连接和后台任务。

## 第一方插件

第一方插件随 Weed 发布，使用同一套插件接口。它们可以使用内部辅助库，但面向 Host 的入口仍遵循公开 manifest 和生命周期规则。

## 开发者体验

MVP 提供：

- C# 插件模板。
- manifest schema。
- 示例插件。
- 调试日志入口。
- 插件打包说明。
- API 文档。
