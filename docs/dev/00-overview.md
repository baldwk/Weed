# 产品边界与技术概览

> [返回开发文档索引](README.md)

## 产品定位

Weed 是 Windows 10+ 上的快捷启动器和效率工具。它以统一搜索窗口为入口，将应用启动、计算、剪切板历史、截图、翻译、文件搜索和外部插件工作流集中在一致的键盘交互中。

产品优先级依次为：低操作成本、响应及时、结果可预测、用户配置明确，以及第一方和外部插件体验一致。

## 当前范围

- `Alt+Space` 唤起主搜索窗口，并提供托盘常驻与单实例激活。
- 支持 Keyword、Hotkey 和 ImplicitQuery 三种入口。
- 支持全局快捷键编辑、插件启停、无前缀查询优先级和插件自定义设置。
- 内置 App Launcher、Calculator、Clipboard、Screenshot、Emoji Search、Translator、File Search 和 Run Command。
- 支持导入 ZIP、DLL、已发布目录和源码目录形式的外部插件。
- 提供更新清单检查、包下载和 SHA256 校验。
- 提供 manifest schema、插件模板、发布脚本与 SmokeTests。

## 非目标

- 外部插件不运行在安全沙箱中，权限字段目前只用于能力声明。
- File Search 不维护自己的全盘文件索引，而是依赖 Everything。
- Translator 不提供自建翻译模型，会调用用户选择的在线服务。
- 当前发行包是 Windows x64 的免安装压缩包，不包含安装器、自动替换程序或代码签名。
- 当前不支持 macOS、Linux 或移动平台。

## 设计原则

- Host 负责统一的窗口、主题、设置、快捷键、查询路由、结果排序与插件生命周期。
- 业务能力优先由插件提供，第一方和外部插件共享公开抽象。
- UI 由 Host 渲染，插件返回结构化结果和动作，不直接拼装主搜索界面。
- 用户设置具有最高持久化优先级，升级时应保留启用状态、快捷键和插件参数。
- 查询、索引和耗时插件调用应支持异步与取消，避免阻塞输入线程。
- 本地数据默认留在用户设备上；涉及网络的插件必须明确说明外发内容。

## 技术基线

- 语言与运行时：C#、.NET 9。
- 桌面 UI：WPF。
- 本地数据：SQLite、FTS5 与 JSON 配置。
- 插件形式：managed .NET DLL。
- 平台能力：Win32 API、Windows Shell API 与 WPF interop。
- 当前目标运行时：`win-x64`，发布包依赖 .NET 9 Desktop Runtime x64。

## 关键术语

- **Host**：Weed 主程序，负责公共体验、调度和插件运行环境。
- **Plugin**：通过公开 SDK 提供查询、命令、设置或常驻服务的 managed DLL。
- **First-party plugin**：随 Weed 发布并由仓库维护的内置插件。
- **External plugin**：用户单独导入的插件，与 Host 同进程运行。
- **Keyword**：带命令前缀的查询入口，例如 `clip hello`。
- **Hotkey**：用户可配置的全局快捷键入口，例如 `Shift+Ctrl+C`。
- **ImplicitQuery**：无前缀查询入口，例如 `1+2` 或 `vscode`。
- **Resident plugin**：启用后需要常驻的插件，例如 Clipboard。
- **Result**：插件返回给 Host 的结构化搜索结果。
- **Action**：用户对结果执行的打开、复制、粘贴、删除或保存等操作。
