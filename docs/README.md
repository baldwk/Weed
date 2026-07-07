# Weed Specification Index

Weed 是 Windows 10+ 上的 Alfred 风格启动器和工作流工具。本文档集定义 MVP 的产品范围、系统设计、插件规范和开发路线。

## 文档列表

- [00-overview.md](00-overview.md): 产品定位、MVP 范围、设计原则和关键术语。
- [01-system-architecture.md](01-system-architecture.md): Host、Core、PluginHost、平台层和第一方插件的职责划分。
- [02-plugin-system.md](02-plugin-system.md): managed DLL 插件规范、manifest、生命周期、权限、打包和版本兼容。
- [03-query-routing-hotkeys.md](03-query-routing-hotkeys.md): Keyword、Hotkey、ImplicitQuery 三种入口，排序公式，历史分和快捷键配置。
- [04-ui-ux.md](04-ui-ux.md): Alfred 风格 UI、主搜索窗、插件面板、设置页、键盘交互和主题规范。
- [05-first-party-plugins.md](05-first-party-plugins.md): MVP 第一方插件规格：AppLauncher、Calculator、Clipboard、Screenshot。
- [06-data-storage.md](06-data-storage.md): 配置、历史、索引、剪切板和大对象存储策略。
- [07-roadmap.md](07-roadmap.md): 分阶段实施路线、验收标准和发布准备。

## 状态

这些文档是 MVP 阶段的产品和技术规格。实现过程中如果 API、数据结构或交互细节发生变化，应同步更新对应 spec。
