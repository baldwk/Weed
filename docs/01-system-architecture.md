# System Architecture

## 总体结构

```text
Weed.App
  WPF 主窗口、托盘、设置页、主题、输入和结果渲染

Weed.Core
  查询路由、排序、历史记录、配置模型、插件注册表

Weed.PluginHost
  manifest 扫描、AssemblyLoadContext、插件生命周期、异常隔离

Weed.Abstractions
  插件 SDK：接口、DTO、manifest 类型、权限和结果模型

Weed.Platform.Windows
  全局热键、Win32 interop、Shell 启动、剪切板、截屏、窗口服务

First-party Plugins
  AppLauncher、Calculator、Clipboard、Screenshot
```

## 项目职责

### Weed.App

- 启动应用和依赖注入容器。
- 创建主搜索窗口、设置窗口、托盘入口和插件面板。
- 处理 `Alt+Space` 唤起、焦点管理、窗口动画和键盘导航。
- 渲染插件返回的结构化结果。
- 管理主题 token、图标资源和 UI 状态。

### Weed.Core

- 加载全局配置和用户配置。
- 维护插件注册表、入口注册表和用户历史。
- 实现 Keyword、Hotkey、ImplicitQuery 路由。
- 计算无前缀查询排序分。
- 记录用户选择行为。
- 提供查询取消、结果合并和增量刷新机制。

### Weed.PluginHost

- 扫描插件目录和 manifest。
- 校验 manifest、SDK 版本和入口类型。
- 为每个插件创建独立 `AssemblyLoadContext`。
- 启动、停止和卸载插件。
- 捕获插件异常并记录诊断信息。
- 将 Host 服务以受控接口传给插件。

### Weed.Abstractions

- 定义插件需要引用的稳定接口。
- 定义查询、结果、动作、快捷键、设置和权限 DTO。
- 避免依赖 WPF、Win32 具体实现和 Host 内部类型。
- 作为插件兼容性的主契约。

### Weed.Platform.Windows

- 注册和注销全局热键。
- 解析开始菜单快捷方式。
- 通过 Shell 打开程序和文件。
- 提供剪切板读写辅助。
- 提供屏幕、窗口、截图和多显示器能力。
- 封装 Win32 句柄、消息和 DPI 相关细节。

## 启动流程

```text
Process start
  -> Load app configuration
  -> Initialize logging
  -> Initialize storage
  -> Load plugin manifests
  -> Initialize enabled plugins
  -> Register global hotkeys
  -> Warm first-party indexes
  -> Show tray and wait for activation
```

## 查询流程

```text
User opens Weed
  -> User types query
  -> Core normalizes text
  -> Router selects activation path
  -> Matching plugins receive QueryContext
  -> Plugins return structured results
  -> Core applies usage score and priority score
  -> App renders ranked results
```

## 命令流程

```text
User selects result or presses hotkey
  -> Core resolves CommandContext
  -> PluginHost dispatches command
  -> Plugin executes action through Host services
  -> Core records usage
  -> App updates or closes UI according to command result
```

## 配置层级

配置按以下顺序合并：

```text
Built-in defaults
  -> Plugin manifest defaults
  -> User settings
  -> Session overrides
```

用户设置拥有最高持久化优先级。插件更新时保留用户对快捷键、启用状态和优先级的配置。

## 插件目录

默认插件目录：

```text
%LOCALAPPDATA%\Weed\Plugins
```

第一方插件随安装包分发，可安装到应用目录或用户插件目录。用户安装的第三方插件位于用户插件目录。

## 日志和诊断

- Host 日志记录启动、插件加载、快捷键注册、查询错误和命令执行错误。
- 每个插件使用独立日志作用域。
- 插件异常包含插件 ID、版本、入口、命令名和查询文本摘要。
- 设置页提供插件诊断入口，用于查看状态、启停记录和最近错误。

## 响应性要求

- UI 线程只处理输入、渲染和轻量状态更新。
- 插件查询、索引、剪切板写入和截图处理在后台任务中执行。
- 输入变化时，旧查询的 `CancellationToken` 会被触发。
- 首批结果可先显示，后续结果增量刷新。
