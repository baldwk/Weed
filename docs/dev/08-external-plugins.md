# 外部插件开发与分发

> [返回开发文档索引](README.md)

## 运行模型

外部插件是实现 `IWeedPlugin` 的 managed .NET DLL。Weed 启动时扫描 `%LOCALAPPDATA%\Weed\plugins` 下的 `manifest.json`，验证入口程序集和类型，并为每个外部插件创建独立 `AssemblyLoadContext`。

`Weed.Abstractions` 由 Host 默认上下文提供，不应重复打包。插件自己的托管依赖、原生库和资源应放在插件发布目录中。

外部插件仍与 Host 处于同一进程。加载上下文用于依赖解析和卸载边界，不是安全沙箱；插件崩溃、阻塞或访问系统资源都可能影响 Weed。

## Manifest

插件包根目录必须包含 `manifest.json`：

```json
{
  "id": "com.example.weather",
  "name": "Weather",
  "version": "0.1.0",
  "sdkVersion": "0.1",
  "assembly": "Example.Weather.dll",
  "entryType": "Example.Weather.WeatherPlugin",
  "runtime": {
    "resident": false
  },
  "activations": [
    {
      "type": "keyword",
      "keyword": "weather",
      "command": "weather.search"
    }
  ],
  "permissions": [
    "network",
    "clipboard.write"
  ]
}
```

关键字段：

- `id`：稳定且全局唯一的插件标识，也是安装目录名和设置命名空间。
- `version`：插件版本，不等同于 Weed 主程序版本。
- `sdkVersion`：目标插件 SDK 版本；当前为 `0.1`。
- `assembly`：相对于包根目录的插件 DLL 路径，不能跳出插件目录。
- `entryType`：实现 `IWeedPlugin` 的 public 类型全名。
- `runtime.resident`：是否需要在启用后启动常驻生命周期。
- `activations`：Keyword、Hotkey 或 ImplicitQuery 入口。
- `permissions`：插件需要的 Host 能力声明，只用于展示和审查，不执行沙箱限制。
- `externalDependencies`：可选的外部程序依赖。Host 可检查进程或内置探针，并按声明尝试启动已安装程序。

使用 [`schemas/manifest.schema.json`](../../schemas/manifest.schema.json) 校验结构。字段与生命周期的完整定义见[插件系统](02-plugin-system.md)。

## 创建项目

从 [`templates/plugin`](../../templates/plugin/README.md) 开始，或创建引用 `Weed.Abstractions` 的类库。仓库内开发可使用项目引用：

```xml
<ProjectReference Include="..\..\Weed.Abstractions\Weed.Abstractions.csproj">
  <Private>false</Private>
</ProjectReference>
```

独立仓库可引用 Weed 发布包中的 `Weed.Abstractions.dll`：

```xml
<Reference Include="Weed.Abstractions">
  <HintPath>..\Weed\Weed.Abstractions.dll</HintPath>
  <Private>false</Private>
</Reference>
```

当前 Host 目标为 .NET 9。普通插件可使用 `net9.0`；使用 Windows API 或 WPF 类型时使用 `net9.0-windows`。入口类必须是 public，并实现 `IWeedPlugin`。

## 发布插件

使用 `dotnet publish` 生成完整目录，不要只复制 `dotnet build` 的主 DLL：

```powershell
dotnet publish .\Example.Plugin.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o .\dist\example.plugin
```

发布目录示例：

```text
example.plugin\
  manifest.json
  Example.Plugin.dll
  Example.Plugin.deps.json
  Dependency.dll
  runtimes\
  assets\
```

将目录中的内容直接压缩，确保 ZIP 根部就是 `manifest.json`：

```powershell
Compress-Archive `
  -Path .\dist\example.plugin\* `
  -DestinationPath .\com.example.weather-0.1.0-win-x64.zip
```

## 导入流程

**Settings > External Plugins** 支持导入：

- 根部包含 `manifest.json` 的 ZIP 或目录。
- 只有一个子目录且该子目录包含 `manifest.json` 的 ZIP 或目录。
- 包含 `manifest.json` 和单个插件 `.csproj` 的源码目录；Weed 会执行 Release、当前 Windows RID、非 self-contained 的 `dotnet publish`。
- 旁边有匹配 manifest 的 DLL。
- 含 public `IWeedPlugin` 类型的单 DLL；如果类型提供静态 `Manifest`，Weed 使用该声明，否则生成最小 manifest。

导入器执行以下步骤：

1. 读取并验证 manifest。
2. 校验 `id`、`assembly`、`entryType` 与路径边界。
3. 将内容复制到 `%LOCALAPPDATA%\Weed\plugins\<manifest.id>`。
4. 用户确认后可替换同 ID 的现有目录。
5. 等待下次启动再加载新插件。

如果源码目录包含多个 `.csproj`，应直接选择插件项目目录，或让插件项目名与 manifest 的 `assembly` 对应。

也可以手动安装已发布目录：

```powershell
$target = "$env:LOCALAPPDATA\Weed\plugins\com.example.weather"
New-Item -ItemType Directory -Force -Path $target
Copy-Item .\dist\example.plugin\* $target -Recurse -Force
```

安装或替换后重启 Weed。

## 独立仓库分发

第三方插件应在自己的仓库中维护版本、文档和 Release。建议结构：

```text
weed-plugin-example\
  src\
  manifest.json
  README.md
  CHANGELOG.md
  .github\workflows\release.yml
```

每个 Release 建议包含：

- `<plugin-id>-<version>-win-x64.zip`
- 面向用户的安装、入口、设置、网络与数据处理说明
- 变更记录
- 可选的 SHA256 校验和

生成校验和：

```powershell
(Get-FileHash -Algorithm SHA256 `
  .\com.example.weather-0.1.0-win-x64.zip).Hash.ToLowerInvariant()
```

不要把开发机的绝对路径、密钥、缓存、模型下载临时文件或不需要的 SDK 程序集放入发布包。

## OCR External Plugin

`External Plugins\Weed.Plugins.Ocr` 是仓库内的外部插件示例，`Weed.App` 不直接引用它。插件使用 RapidOCRLib 与 PP-OCRv5 中文模型。

构建源码：

```powershell
dotnet build "External Plugins\Weed.Plugins.Ocr\Weed.Plugins.Ocr.csproj"
```

生成包含模型和运行依赖的导入包：

```powershell
powershell -ExecutionPolicy Bypass `
  -File scripts\package-ocr-plugin.ps1 `
  -FetchModels
```

输出：

```text
artifacts\plugins\weed.ocr\
artifacts\plugins\weed.ocr.zip
artifacts\plugins\weed.ocr-0.1.0-win-x64.zip
artifacts\plugins\weed.ocr-0.1.0.plugin-release.json
```

模型约 21 MiB；包含 RapidOCR、ONNX、OpenCV 运行时与模型的 ZIP 约 60 MiB，实际大小随依赖版本变化。`-FetchModels` 将模型下载到打包目录，不写入源码控制。

导入并重启后：

- `ocr`：显示截图识别和图片识别入口。
- `ocr "C:\path\image.png"`：识别本地图片。
- `Shift+Alt+O`：选择屏幕区域并识别。

默认结果动作复制识别文字；保存并打开文本文件是次要动作。面向用户的说明见 [OCR 插件 README](../../External%20Plugins/Weed.Plugins.Ocr/README.md)。

## 发布前检查

- 从干净目录解压 ZIP，确认根部存在 manifest、DLL、`.deps.json` 与全部依赖。
- 使用 schema 校验 manifest，并确认版本与文件名一致。
- 验证首次导入、覆盖导入、重启加载、禁用、设置和卸载。
- 验证依赖缺失、网络失败、无效输入和取消操作能返回可理解的结果。
- 检查日志不包含密钥、剪切板全文、翻译正文或其他不必要的敏感数据。
- 记录插件会访问的文件、网络服务、剪切板和屏幕能力。

## 故障排查

- **插件未出现**：重启 Weed，检查插件启用状态与详情页日志。
- **依赖加载失败**：使用 `dotnet publish` 而不是只分发 `dotnet build` 输出。
- **ZIP 能导入但不能加载**：确认 `manifest.json` 位于根部，并且 `assembly` 指向包内真实 DLL。
- **源码导入失败**：确认安装了 .NET 9 SDK，且目录中能唯一定位插件项目。
- **原生库加载失败**：检查 `runtimes\win-x64\native` 资源和进程架构。
- **版本替换后仍是旧行为**：确认勾选替换现有插件，然后重启 Weed。
