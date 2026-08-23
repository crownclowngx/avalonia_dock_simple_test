# 验证与排错

> 本页按 V3 G14 已封板基线编写。四插件已依次通过最终 Workspace、注册所有权、互斥激活、修订保存、
> 私有消息、Headless UI 与真实 ZIP 验收；DaTang 覆盖双 Document 与窗口端口，MySmallTools 覆盖
> 原生资源与关闭令牌，BiliDownloader 覆盖 Document + Tool + Lifecycle + readiness 大型对象图。

验证新插件时应从“目录和清单”开始，再检查模块、扩展元数据和界面行为。这样能够在最接近故障来源的位置停止，而不是从空白界面反推所有可能原因。

## 最小验收清单

### 构建与加载

- [ ] Host 和插件使用相同的配置与目标框架构建；
- [ ] `Controls/<PluginFolder>/` 是插件独占目录；
- [ ] 根目录同时存在 `plugin.manifest.json` 和清单声明的入口 DLL；
- [ ] 入口旁存在同名 `.deps.json` 和 PDB；
- [ ] 项目声明 `ManagedPlugin=true`，清单来自构建输出而非源码树手写副本；
- [ ] 清单版本与入口 `AssemblyVersion` 一致；manifest 是唯一插件身份来源；
- [ ] `entryPoint.type` 与 public、非抽象、非泛型且具有 public 无参构造的最终 UI SDK 模块完整名称完全一致；
- [ ] 构建探针直接验证最终 UI SDK `IPluginModule`，项目没有 Legacy 或过渡入口属性；
- [ ] 模块通过 `IPluginRegistration` 一次登记模型、View、Descriptor 和可选 Lifecycle；
- [ ] Document/Tool ID 分别使用 `{PluginId}.document.*` / `{PluginId}.tool.*`，且不手工登记 Host Port 或贡献根类型；
- [ ] 插件目录不包含 Legacy、Core/UI SDK、Avalonia、Dock、Host 或其他宿主共享程序集；
- [ ] 宿主“插件状态”Tool 将该插件显示为已加载，没有拒绝原因。

### Document

- [ ] 插件菜单能显示 Document 的 `MenuCategory` 和 `DisplayName`；
- [ ] 连续创建两个 Document 会得到两个独立标签；
- [ ] 两个标签的可变状态互不影响；
- [ ] 关闭标签后，Scoped ViewModel 及其可释放依赖由宿主释放；
- [ ] View 由 `AddDocument<TDocument,TView>()` 或 `AddPersistableDocument<TDocument,TView>()` 同时声明，`DataContext` 为对应模型。

### Tool

- [ ] Tool 出现在 `ToolDescriptor.DockSide` 指定的方向；
- [ ] 同一 Tool 只创建一个实例；
- [ ] 点击关闭后 Tool 被隐藏，而不是销毁；
- [ ] 从工具管理入口恢复后仍保持隐藏前的实例状态。

### 更新插件

- [ ] 替换 DLL、依赖或清单后完整退出并重启宿主；
- [ ] 新版本仍保留已经发布的 Plugin、Document 和 Tool 稳定 ID；
- [ ] V3 只保留规范主 ID，不新增旧 ID 别名或兼容读取器。

## 推荐命令

在仓库根目录执行最短构建和启动验证：

```powershell
dotnet build Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug
dotnet build Plugins/QuickStartPlugin/QuickStartPlugin/QuickStartPlugin.csproj -c Debug
dotnet run --project Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug --no-build
```

实际插件进入仓库后，应为其增加模块发现、稳定 ID、Document Scope、Tool 单例和真实输出目录加载覆盖。宿主插件集成测试可单独运行：

```powershell
dotnet test Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj -c Release
```

V3 G9–G12 使用以下当前入口分别验收四插件；G2–G8 专项继续保护各自的平台语义：

```powershell
dotnet test Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj -c Release
dotnet test Plugins/MyPlugTest/MyPlugTest.Tests/MyPlugTest.Tests.csproj -c Release
dotnet test Plugins/DaTangAccountingHelpPlug/DaTangAccountingHelpPlug.Tests/DaTangAccountingHelpPlug.Tests.csproj -c Release
dotnet test Plugins/MySmallTools/MySmallTools.Tests/MySmallTools.Tests.csproj -c Release
dotnet test Plugins/BiliDownloader/BiliDownloader.Tests/BiliDownloader.Tests.csproj -c Release
.\scripts\Test-RevisionedDocumentSave.ps1 -Configuration Release -NoRestore
.\scripts\Test-ExclusiveDocumentActivation.ps1 -Configuration Release -NoRestore
.\scripts\Test-PluginRegistrationOwnership.ps1 -Configuration Release -NoRestore
.\scripts\Test-MyPlugTestV3.ps1 -Configuration Release -NoRestore
.\scripts\Test-DaTangAccountingHelpPlugV3.ps1 -Configuration Release -NoRestore
.\scripts\Test-MySmallToolsV3.ps1 -Configuration Release -NoRestore
.\scripts\Test-BiliDownloaderV3.ps1 -Configuration Release -NoRestore
```

V2 历史文档继续用于审计，但已由当前阶段删除的活动脚本不保留兼容包装入口。V3 G9–G12 单独运行时不访问真实账号、
真实 Bilibili、Windows CI/Smoke、ReleaseAcceptance 或发布门禁；G11 的本地真实媒体 Harness 是资源门禁，
不联网，也不是发布验收。

正式封板复验仅从 Windows x64 干净提交运行：

```powershell
.\scripts\Invoke-HostV3ReleaseGate.ps1
```

该入口会执行两轮隔离完整矩阵和真实窗口 Smoke，但不会上传、打标签或访问外部账号。

现有测试范围与输出位置见 [MyAvaloniaManagement 测试说明](../reference/myavalonia-management-tests.md)。新增真实插件时，还应更新 [`CurrentManagedPluginLoadingTests`](../../Host/MyAvaloniaManagement.PluginTests/CurrentManagedPluginLoadingTests.cs) 的预期插件集合，而不是仅靠手工打开界面验收。

本地验证单插件构建与全矩阵时运行：

```powershell
.\scripts\Build-ManagedPluginPackage.ps1 -Project <插件.csproj> -Configuration Release
.\scripts\Test-ManagedPluginPackages.ps1 -Configuration Release
```

第一个命令只生成一个独立 ZIP，第二个自动发现全部 `ManagedPlugin=true` 项目并做两轮确定性构建、
契约负例、最终 ZIP 宿主加载和聚合 `summary.json`。它们都使用隔离部署根，不触碰真实 `Controls`
或用户数据；这些本地测试包不构成发布制品。

## 检查本地 Markdown 链接

下面的 PowerShell 片段递归检查 Quick Start 中不含通配符的本地链接。它忽略外部 URL 和纯页内锚点：

```powershell
$files = Get-ChildItem docs/quick-start -Recurse -Filter '*.md' -File
$broken = foreach ($file in $files) {
    $content = Get-Content -Raw -LiteralPath $file.FullName
    foreach ($match in [regex]::Matches($content, '\]\((?<target>[^)]+)\)')) {
        $target = ($match.Groups['target'].Value -split '#', 2)[0]
        if ($target -and $target -notmatch '^(https?://|mailto:)') {
            $path = Join-Path $file.DirectoryName ([uri]::UnescapeDataString($target))
            if (-not (Test-Path -LiteralPath $path)) {
                "$($file.FullName) -> $target"
            }
        }
    }
}
if ($broken) { $broken; throw '发现失效的本地 Markdown 链接。' }
```

## 从哪里查看失败原因

先打开宿主的 **插件状态** Tool。可恢复的单插件加载错误会按插件目录展示；如果组合错误阻断整个工作台，启动错误窗口会显示稳定错误码和日志位置。

诊断日志默认写入：

```text
%LOCALAPPDATA%\MyAvaloniaManagement\v2\Diagnostics\session-*.jsonl
```

设置 `MYAVALONIA_DATA_DIRECTORY` 后，诊断写入该数据根目录下的 `Diagnostics/`。测试或排错时可以使用独立目录，避免读取和覆盖正式用户数据。

诊断默认只显示稳定错误码、阶段、经过校验的插件/程序集/稳定 ID、版本、异常类型和受控耗时。
插件异常消息、文档正文、凭据、URL、请求响应和完整路径不会进入插件状态、启动失败摘要、剪贴板或
JSONL。因此排错时应优先使用错误码、异常类型和本页处理方向，不要期待日志包含原始异常正文。

只有在用户明确确认可能暴露本机敏感信息、且能够控制终端和 Trace 监听器时，才可为当前进程精确设置：

```powershell
$env:MYAVALONIA_ENABLE_SENSITIVE_DIAGNOSTICS = '1'
```

该开关只把带显著警告的原始异常写入临时 Trace/stderr，不会改变 UI 或 JSONL。它不写入配置，
`true` 等值不会开启；排错结束后关闭进程，或执行
`Remove-Item Env:MYAVALONIA_ENABLE_SENSITIVE_DIAGNOSTICS`。不要在 Release 门禁、共享日志收集器或
普通启动脚本中设置此变量。

## 常见错误码

| 错误码 | 常见原因 | 处理方向 |
| --- | --- | --- |
| `PLUGIN_MANIFEST_MISSING` | 插件根目录没有清单 | 确认项目声明 `ManagedPlugin=true` 并使用公共 Target 构建；不要恢复手写清单 |
| `PLUGIN_MANIFEST_INVALID` | 字段拼写、重复字段、注释、尾逗号、版本或入口格式不合法 | 对照严格清单逐字段检查，不要依赖宽松 JSON 解析器 |
| `PLUGIN_MANIFEST_SCHEMA_UNSUPPORTED` | `schemaVersion` 不是宿主支持的版本 | 当前只使用 `schemaVersion: 2`；不保留 v1 reader |
| `PLUGIN_SDK_INCOMPATIBLE` | 当前 Core/UI SDK 版本不在清单的单一区间 | 针对目标 SDK 重新编译验证，或修正已经验证过的左闭右开区间 |
| `PLUGIN_MANIFEST_DESCRIPTION_MISMATCH` | 清单版本或入口程序集身份不一致 | 对齐 `pluginVersion`、`AssemblyVersion` 和入口名称 |
| `PLUGIN_ENTRY_INVALID` | 入口程序集/类型不存在或大小写不符，或入口类型不可执行 | 核对精确完整类型名，并保证类型 public、非抽象、非泛型、实现最终 UI SDK 接口且有 public 无参构造 |
| `PLUGIN_DEPENDENCY_MANIFEST_MISSING` | 入口缺少同名 `.deps.json` | 启用依赖文件生成并把 deps 作为必需发布资产 |
| `PLUGIN_ASSEMBLY_LOAD_FAILED` / `PLUGIN_TYPE_PREFLIGHT_FAILED` | 私有依赖缺失、RID 资产错误或类型无法完整加载 | 检查 `.deps.json`、私有托管依赖和原生资产是否完整 |
| `PLUGIN_SHARED_ASSEMBLY_MISMATCH` | 插件私带了不兼容的宿主共享程序集 | 从插件包删除 Common 及共享闭包，并用匹配契约重新编译 |
| `PLUGIN_ID_INVALID` / `PLUGIN_ID_DUPLICATE` | ID 不规范或与其他插件重复 | 使用规范命名空间并保持全局唯一 |
| `DOCUMENT_ID_OWNER_MISMATCH` / `TOOL_ID_OWNER_MISMATCH` | Document/Tool ID 不属于 manifest 插件或使用了错误贡献种类 | 分别使用精确的 `{PluginId}.document.*` / `{PluginId}.tool.*` 命名空间 |
| `PLUGIN_HOST_SERVICE_REGISTRATION_FORBIDDEN` | 插件用普通或 keyed DI 注册影子覆盖 Host Port | 删除该注册；Host 会在模块 Seal 通过后最终追加端口 |
| `PLUGIN_CONTRIBUTION_SERVICE_REGISTRATION_FORBIDDEN` | 插件手工登记了已声明的 Document/Tool/Lifecycle 根类型 | 只调用 `AddDocument`、`AddTool` 或 `UseLifecycle`，不要重复登记根类型 |
| `VIEW_MODEL_REGISTRATION_DUPLICATE` | 同一个 ViewModel 显式映射到多个 View | 每个动态 ViewModel 只保留一项 `AddView` |
| `PLUGIN_SERVICE_REGISTRATION_FAILED` | 模块 `Configure` 抛错 | 检查当前插件注册；失败只隔离当前插件 |
| `PLUGIN_CONTAINER_BUILD_FAILED` | 当前插件 Provider 构建或宿主可见单例激活失败 | 检查私有依赖、生命周期和 Scope 关系 |
| `EXTENSION_ACTIVATION_FAILED` | Registry 读取策略/生命周期或元数据失败 | 检查贡献声明和元数据实现 |
| `VIEW_CREATION_FAILED` | 已登记 View 的无参构造抛出异常 | 检查 `InitializeComponent` 与 XAML 资源；业务依赖应放入 ViewModel |

## 必须遵守的边界

1. **清单字段严格区分大小写。** 不要添加宿主尚未定义的自解释字段。
2. **manifest 是身份唯一事实源。** 扩展 ID 必须属于 manifest `pluginId` 命名空间；模块和 Lifecycle 不再声明身份。
3. **只执行清单精确入口。** 入口模块需要 public 无参构造并只在组合期使用 Context；同程序集中的其他模块不会被扫描或执行。
4. **Document Scope 归宿主所有。** 插件不保存、不复用、不主动释放宿主创建的 `IServiceScope`。
5. **Tool 是单例。** 关闭表示隐藏，不要把关键清理逻辑只放在 View 的关闭事件中。
6. **共享契约不随插件交付。** Common 和宿主共享依赖来自默认加载上下文，插件目录只放私有依赖。
7. **不支持热更新。** 目录扫描和加载上下文以进程为边界，替换文件后必须重启。
8. **稳定 ID 不跟着显示名和类名变化。** 已发布 ID 的变更属于数据和布局迁移，不是普通重命名。
9. **没有隐式发现。** 未调用 `AddDocument`、`AddTool`、`AddView` 或 `AddLifecycle` 的类型对宿主不可见。

更完整的加载、隔离和生命周期规则见[宿主—插件架构评审](../design/host-plugin-architecture-review.md)与[主项目兼容约束](../../Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md)。
