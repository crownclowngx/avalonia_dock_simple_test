# 验证与排错

验证新插件时应从“目录和清单”开始，再检查模块、扩展元数据和界面行为。这样能够在最接近故障来源的位置停止，而不是从空白界面反推所有可能原因。

## 最小验收清单

### 构建与加载

- [ ] Host 和插件使用相同的配置与目标框架构建；
- [ ] `Controls/<PluginFolder>/` 是插件独占目录；
- [ ] 根目录同时存在 `plugin.manifest.json` 和清单声明的入口 DLL；
- [ ] 清单版本与入口 `AssemblyVersion` 一致；manifest 是唯一插件身份来源；
- [ ] 模块通过 Context 显式登记全部 Document、Tool、View 和可选 Lifecycle；
- [ ] 插件目录不包含 `MyAvaloniaManagementCommon.dll` 或其他宿主共享程序集；
- [ ] 宿主“插件状态”Tool 将该插件显示为已加载，没有拒绝原因。

### Document

- [ ] 插件菜单能显示 Document 的 `MenuCategory` 和 `DisplayName`；
- [ ] 连续创建两个 Document 会得到两个独立标签；
- [ ] 两个标签的可变状态互不影响；
- [ ] 关闭标签后，Scoped ViewModel 及其可释放依赖由宿主释放；
- [ ] ViewModel 与 View 已通过 `AddView<TViewModel,TView>()` 显式映射。

### Tool

- [ ] Tool 出现在 `ToolMetadata.DockSide` 指定的方向；
- [ ] 同一 Tool 只创建一个实例；
- [ ] 点击关闭后 Tool 被隐藏，而不是销毁；
- [ ] 从工具管理入口恢复后仍保持隐藏前的实例状态。

### 更新插件

- [ ] 替换 DLL、依赖或清单后完整退出并重启宿主；
- [ ] 新版本仍保留已经发布的 Plugin、Document 和 Tool 稳定 ID；
- [ ] 有意迁移旧 ID 时仅把旧值放入元数据别名，新建和保存仍使用主 ID。

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

提交前运行完整宿主门禁；需要真实窗口冒烟时追加第二条：

```powershell
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release -WindowsSmoke
```

现有测试范围与输出位置见 [MyAvaloniaManagement 测试说明](../reference/myavalonia-management-tests.md)。新增真实插件时，还应更新 [`CurrentManagedPluginLoadingTests`](../../Host/MyAvaloniaManagement.PluginTests/CurrentManagedPluginLoadingTests.cs) 的预期插件集合，而不是仅靠手工打开界面验收。

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
%LocalAppData%\MyAvaloniaManagement\Diagnostics\session-*.jsonl
```

设置 `MYAVALONIA_DATA_DIRECTORY` 后，诊断写入该数据根目录下的 `Diagnostics/`。测试或排错时可以使用独立目录，避免读取和覆盖正式用户数据。

## 常见错误码

| 错误码 | 常见原因 | 处理方向 |
| --- | --- | --- |
| `PLUGIN_MANIFEST_MISSING` | 插件根目录没有清单 | 把 `plugin.manifest.json` 复制到入口 DLL 同级目录 |
| `PLUGIN_MANIFEST_INVALID` | 字段拼写、重复字段、注释、尾逗号、版本或入口格式不合法 | 对照严格清单逐字段检查，不要依赖宽松 JSON 解析器 |
| `PLUGIN_MANIFEST_SCHEMA_UNSUPPORTED` | `schemaVersion` 不是宿主支持的版本 | 当前使用 `schemaVersion: 1` |
| `PLUGIN_HOST_API_INCOMPATIBLE` / `PLUGIN_COMMON_CONTRACT_INCOMPATIBLE` | 当前宿主版本不在清单区间 | 针对目标版本重新编译验证，或修正已经验证过的区间 |
| `PLUGIN_MANIFEST_DESCRIPTION_MISMATCH` | 清单版本或入口程序集身份不一致 | 对齐 `pluginVersion`、`AssemblyVersion` 和入口名称 |
| `PLUGIN_ENTRY_INVALID` | 清单入口不存在、包含路径或不是托管程序集 | 每个插件使用独立目录，并提供清单声明的根级入口 |
| `PLUGIN_DEPENDENCY_MANIFEST_MISSING` | 入口缺少同名 `.deps.json` | 启用依赖文件生成并把 deps 作为必需发布资产 |
| `PLUGIN_ASSEMBLY_LOAD_FAILED` / `PLUGIN_TYPE_PREFLIGHT_FAILED` | 私有依赖缺失、RID 资产错误或类型无法完整加载 | 检查 `.deps.json`、私有托管依赖和原生资产是否完整 |
| `PLUGIN_SHARED_ASSEMBLY_MISMATCH` | 插件私带了不兼容的宿主共享程序集 | 从插件包删除 Common 及共享闭包，并用匹配契约重新编译 |
| `PLUGIN_MODULE_MULTIPLE` | 一个入口程序集实现了多个 `IPluginModule` | 只保留一个 public、可实例化模块入口 |
| `PLUGIN_MODULE_MISSING` | 入口程序集没有具体 `IPluginModule` | 增加唯一模块，不能只交付 Document/Tool 策略 |
| `PLUGIN_MODULE_CONSTRUCTOR_INVALID` | 唯一模块缺少 public 无参构造 | 模块仅作为 DI 建立前的引导对象，恢复 public 无参构造 |
| `PLUGIN_ID_INVALID` / `PLUGIN_ID_DUPLICATE` | ID 不规范或与其他插件重复 | 使用规范命名空间并保持全局唯一 |
| `EXTENSION_OWNER_MISMATCH` / `EXTENSION_METADATA_INVALID` | Document/Tool ID 不属于本插件，或主 ID/别名冲突 | 统一使用插件自己的 ID 前缀，检查所有元数据和迁移别名 |
| `CONTRIBUTION_REGISTRATION_BYPASS` | 通过 `context.Services` 直接注册了贡献接口 | 删除直接接口注册，改用对应 `context.Add*` 方法 |
| `PLUGIN_HOST_SERVICE_MUTATION` | 插件删除、替换、重排已有 DI 描述符，或追加了宿主保护类型 | 只追加插件私有服务；不要使用 Remove/Replace/Clear 覆盖宿主注册 |
| `VIEW_MODEL_REGISTRATION_DUPLICATE` | 同一个 ViewModel 显式映射到多个 View | 每个动态 ViewModel 只保留一项 `AddView` |
| `PLUGIN_SERVICE_REGISTRATION_FAILED` / `EXTENSION_ACTIVATION_FAILED` | 模块配置抛错，或策略/生命周期构造和元数据读取失败 | 检查 DI 注册、贡献声明和构造依赖 |
| `VIEW_CREATION_FAILED` | 已登记 View 的无参构造抛出异常 | 检查 `InitializeComponent` 与 XAML 资源；业务依赖应放入 ViewModel |

## 必须遵守的边界

1. **清单字段严格区分大小写。** 不要添加宿主尚未定义的自解释字段。
2. **manifest 是身份唯一事实源。** 扩展 ID 必须属于 manifest `pluginId` 命名空间；模块和 Lifecycle 不再声明身份。
3. **一个程序集只能有一个模块。** 模块需要 public 无参构造并只在组合期使用 Context；策略和 Lifecycle 可以使用构造注入。
4. **Document Scope 归宿主所有。** 插件不保存、不复用、不主动释放宿主创建的 `IServiceScope`。
5. **Tool 是单例。** 关闭表示隐藏，不要把关键清理逻辑只放在 View 的关闭事件中。
6. **共享契约不随插件交付。** Common 和宿主共享依赖来自默认加载上下文，插件目录只放私有依赖。
7. **不支持热更新。** 目录扫描和加载上下文以进程为边界，替换文件后必须重启。
8. **稳定 ID 不跟着显示名和类名变化。** 已发布 ID 的变更属于数据和布局迁移，不是普通重命名。
9. **没有隐式发现。** 未调用 `AddDocument`、`AddTool`、`AddView` 或 `AddLifecycle` 的类型对宿主不可见。

更完整的加载、隔离和生命周期规则见[宿主—插件架构评审](../design/host-plugin-architecture-review.md)与[主项目兼容约束](../../Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md)。
