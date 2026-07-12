# 数据与存储

> [返回开发文档索引](README.md)

## 存储根目录

Weed 将可漫游的用户配置和本机数据分开保存：

```text
%APPDATA%\Weed\
  settings.json
  hotkeys.json
  plugins.json
  plugin-settings\

%LOCALAPPDATA%\Weed\
  weed.db
  logs\
  plugins\
  plugins-data\
  cache\
  clipboard-objects\
  updates\
```

默认截图目录为 `%USERPROFILE%\Pictures\Weed`。`clipboard-objects` 是 Host 预留路径；当前 Clipboard 插件的大对象位于自己的 `plugins-data\weed.clipboard\objects` 目录。

## 全局配置

### settings.json

保存应用级设置：

```json
{
  "theme": "system",
  "showTrayIcon": true,
  "launchAtStartup": false,
  "autoCheckUpdates": false,
  "updateManifestUrl": "",
  "externalPluginRegistryUrl": "",
  "mainHotkey": "Alt+Space",
  "closeOnLostFocus": true
}
```

### hotkeys.json

以 `<pluginId>:<command>` 为键保存插件热键覆盖：

```json
{
  "weed.clipboard:clipboard.show": {
    "keys": "Shift+Ctrl+C",
    "enabled": true
  },
  "weed.screenshot:screenshot.region": {
    "keys": "Shift+Alt+A",
    "enabled": true
  }
}
```

### plugins.json

保存插件启用状态与 ImplicitQuery 优先级：

```json
{
  "weed.appLauncher": {
    "enabled": true,
    "priority": 0
  }
}
```

`priority` 被限制在 `0` 到 `100`，只参与无前缀查询排序。

### plugin-settings\<pluginId>.json

每个插件使用独立 JSON 文件保存自身设置。Host 根据 `IPluginSettingsProvider` 返回的定义渲染和持久化字段，不解释具体业务含义。

包含密钥的插件设置当前仍以明文 JSON 保存在用户配置目录。日志不得输出凭据；未来引入凭据库时应保留设置键兼容性并提供迁移。

## Core 数据库

`%LOCALAPPDATA%\Weed\weed.db` 保存使用历史与 schema migration。当前核心表：

```sql
CREATE TABLE usage_history (
  plugin_id TEXT NOT NULL,
  result_id TEXT NOT NULL,
  command_id TEXT NOT NULL,
  selected_count INTEGER NOT NULL DEFAULT 0,
  last_selected_at TEXT,
  PRIMARY KEY (plugin_id, result_id, command_id)
);
```

每次成功执行结果后更新选择次数与时间，查询排序时映射为 UsageScore。

## 插件数据

插件通过 Host 获取两个隔离目录：

```text
%LOCALAPPDATA%\Weed\plugins-data\<pluginId>
%LOCALAPPDATA%\Weed\cache\<pluginId>
```

- App Launcher 将可重建索引保存在 `plugins-data\weed.appLauncher\app-launcher.db`，图标缓存在 `cache\weed.appLauncher\icons`。
- Clipboard 将元数据与 FTS 索引保存在 `plugins-data\weed.clipboard\clipboard.db`，图片、HTML 和 RTF 等对象保存在同目录下的 `objects`。
- OCR 将区域截图与文本结果保存在 `plugins-data\weed.ocr`。

插件 ID 在生成目录名时会替换 Windows 不允许的文件名字符。

## Clipboard 保留策略

Clipboard 默认保留 180 天、最多 100,000 条记录，大对象总量上限为 2,048 MB。置顶项优先保留；删除或清理记录时，应同步删除不再引用的对象文件。

可存储的内容类型包括 `text`、`image`、`files`、`rtf` 和 `html`。数据库保存可搜索文本、内容 hash、对象路径、时间、置顶状态和大小等元数据，原始大对象不直接写入主表。

## 截图输出

截图默认保存到 `%USERPROFILE%\Pictures\Weed`，也可由 `defaultSaveDirectory` 覆盖。文件名使用 `Screenshot-{yyyyMMdd-HHmmss}` 形式，扩展名由 PNG/JPEG 设置决定。

截图插件还持久化 JPEG 质量、最大保存文件大小、默认标注颜色和线宽。截图编辑中的临时状态只存在于当前会话。

## 外部插件与更新

- 外部插件安装目录：`%LOCALAPPDATA%\Weed\plugins\<manifest.id>`。
- 更新下载目录：`%LOCALAPPDATA%\Weed\updates`。
- 覆盖或删除应用程序目录不会自动删除上述用户数据。

导入器必须验证 manifest 与程序集路径均位于插件包内，避免通过相对路径写入目标目录之外。

## 迁移与清理

- SQLite 数据库使用 `schema_migrations` 记录已应用版本，迁移必须按序且可诊断。
- 配置写入先生成 `.tmp` 文件，再原子替换目标文件。
- 插件负责清理自己的过期记录、对象和缓存；Core 不应猜测插件数据格式。
- 日志、更新包和可重建缓存应提供独立清理策略，不得连带删除用户配置或已导入插件。
- 破坏性 schema 或目录调整必须提供从上一正式版本升级的迁移与回滚说明。
