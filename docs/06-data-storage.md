# Data Storage

## 存储根目录

用户数据默认位于：

```text
%APPDATA%\Weed
```

缓存和可重建数据默认位于：

```text
%LOCALAPPDATA%\Weed
```

建议目录：

```text
%APPDATA%\Weed\
  settings.json
  hotkeys.json
  plugins.json

%LOCALAPPDATA%\Weed\
  weed.db
  logs\
  plugins\
  cache\
  clipboard-objects\
  screenshots\
```

## 数据库

Weed 使用 SQLite，启用 WAL 模式。

数据库职责：

- 用户历史。
- 插件状态。
- AppLauncher 索引缓存。
- Clipboard 元数据和全文索引。
- 迁移版本。

## 配置文件

### settings.json

保存全局设置：

```json
{
  "theme": "system",
  "showTrayIcon": true,
  "launchAtStartup": false,
  "mainHotkey": "Alt+Space"
}
```

### hotkeys.json

保存用户快捷键覆盖：

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

保存插件启用状态和 implicit 优先级：

```json
{
  "weed.appLauncher": {
    "enabled": true,
    "priority": 0
  },
  "weed.calculator": {
    "enabled": true,
    "priority": 0
  }
}
```

`priority` 只参与 ImplicitQuery 排序。

## 用户历史

历史表记录用户选择结果：

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

每次执行结果后更新 `selected_count` 和 `last_selected_at`。查询排序时映射为 `UsageScore`。

## AppLauncher 索引

```sql
CREATE TABLE app_entries (
  id TEXT PRIMARY KEY,
  display_name TEXT NOT NULL,
  normalized_name TEXT NOT NULL,
  pinyin TEXT,
  pinyin_initials TEXT,
  shortcut_path TEXT NOT NULL,
  target_path TEXT,
  arguments TEXT,
  working_directory TEXT,
  icon_path TEXT,
  indexed_at TEXT NOT NULL
);
```

索引缓存可重建。AppLauncher 启动时优先读取缓存，再按策略刷新。

## Clipboard 存储

```sql
CREATE TABLE clipboard_items (
  id TEXT PRIMARY KEY,
  content_hash TEXT NOT NULL,
  kind TEXT NOT NULL,
  title TEXT,
  text_content TEXT,
  object_path TEXT,
  source_format TEXT,
  created_at TEXT NOT NULL,
  last_used_at TEXT,
  pinned INTEGER NOT NULL DEFAULT 0,
  size_bytes INTEGER NOT NULL DEFAULT 0
);
```

全文索引：

```sql
CREATE VIRTUAL TABLE clipboard_items_fts
USING fts5(title, text_content, content='clipboard_items', content_rowid='rowid');
```

内容类型：

```text
text
image
files
rtf
html
```

## 大对象存储

图片、富文本原始内容和长文件列表保存到对象目录：

```text
%LOCALAPPDATA%\Weed\clipboard-objects
```

对象路径由内容 hash 派生：

```text
clipboard-objects/
  ab/
    cd/
      abcdef....bin
```

数据库保存 `object_path`、`content_hash` 和 `size_bytes`。

默认策略：

```text
Clipboard records: 100,000 items or 180 days
Clipboard objects: 2 GB total
```

用户可在设置页调整记录数量、保留天数和对象空间上限。

## Screenshot 输出

截图默认保存目录：

```text
%USERPROFILE%\Pictures\Weed
```

设置项：

- 默认格式：PNG。
- JPEG 质量。
- 文件命名模板。
- 是否复制到剪切板。
- 是否保存到文件。

文件命名模板示例：

```text
Screenshot-{yyyyMMdd-HHmmss}.png
```

## 迁移

数据库使用 schema version：

```sql
CREATE TABLE schema_migrations (
  version INTEGER PRIMARY KEY,
  applied_at TEXT NOT NULL
);
```

迁移规则：

- 应用启动时检查版本。
- 迁移按版本顺序执行。
- 迁移失败时保留原数据库文件。
- 日志记录迁移版本和耗时。

## 清理

清理任务由对应插件或 Core 发起：

- Clipboard 清理过期记录和未引用对象。
- AppLauncher 可清理不存在的快捷方式缓存。
- Logs 按大小和天数滚动。
- Screenshot 临时文件在编辑器关闭后释放。
