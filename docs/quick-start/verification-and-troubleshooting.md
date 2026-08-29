# 编译、打包、真实 Host 验收与排错

> 当前统一入口为 `dotnet run --project tools/MyAvaloniaManagement.Gate -- verify`；按需增加
> `--scope host|workflow|workbench`。本文出现的历史 `scripts/*.ps1` 命令已经退役。

验证应分为四层：源码构建、Standalone、独立 ZIP、真实 Host。前一层通过不能替代后一层。

## 1. 源码构建和测试

在插件解决方案根目录执行：

```powershell
dotnet restore
dotnet build -c Debug -warnaserror
dotnet test -c Debug --no-build
```

发布前再运行：

```powershell
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
```

最低测试建议：

- 每个 `IPluginDocument.InitializeAsync` 的 New/Restore 合法分支；
- 同一种 Document 两个实例状态互不影响；
- 持久化 Document 的 Revision、Dirty 和旧修订确认；
- Module 登记了预期 Document/Tool ID，且无重复；
- Tool 多次解析得到同一个 Model；
- 插件私有服务的 scoped/singleton 生命周期符合预期。

## 2. Standalone 调试

```powershell
dotnet run --project src/ExamplePlugin.Standalone
```

在 Rider 中把 `ExamplePlugin.Standalone` 设为启动项目并使用 Debug。多贡献工作台应至少人工检查：

- 左侧能看到 Module 登记的全部 Document 和 Tool；
- 连续打开同一种 Document 两次得到两个独立页面；
- 两个页面的输入互不影响；
- 关闭一个页面不会关闭另一个页面；
- Tool 隐藏后再次显示仍保留状态；
- View 的 `DataContext` 类型与注册的 Model 一致；
- 缺失 Host Port 时显示明确“不支持”，而不是静默使用 null。

Standalone 只证明插件自身 UI/对象图，不能证明 ZIP 能被 Host 加载。

## 3. 生成独立插件包

```powershell
dotnet msbuild src/ExamplePlugin.Plugin/ExamplePlugin.Plugin.csproj `
  -t:BuildManagedPluginPackage `
  -p:Configuration=Release
```

成功后应得到：

```text
src/ExamplePlugin.Plugin/artifacts/managed-plugin-packages/
├─ ExamplePlugin.Plugin-1.0.0-win-x64.zip
└─ ExamplePlugin.Plugin-1.0.0-win-x64.manifest.json
```

ZIP 解压后的插件目录至少包含：

```text
Controls/ExamplePlugin/
├─ ExamplePlugin.Plugin.dll
├─ ExamplePlugin.Plugin.deps.json
├─ ExamplePlugin.Plugin.pdb
└─ plugin.manifest.json
```

插件自己的私有依赖和原生资产可以额外存在。以下 Host 共享程序集不应进入 ZIP：

- `MyAvaloniaManagement.PluginSdk*`；
- Avalonia、Dock、Semi、Ursa；
- `Microsoft.Extensions.*`；
- Host 可执行程序或 Host 业务程序集。

Build 包会自动做共享程序集检查、入口探针、manifest 校验和最终 ZIP 哈希复核；不要绕过它手工压缩
`bin` 目录。

新增运行时 NuGet 包后，逐项确认：根 `Directory.Packages.props` 有 `PackageVersion`、Plugin `.csproj` 有
`PackageReference`，且同一 Plugin `.csproj` 有准确包 ID 的 `ManagedPluginPrivatePackage`。对带传递运行时
依赖的包执行：

```powershell
dotnet list src/ExamplePlugin.Plugin/ExamplePlugin.Plugin.csproj package --include-transitive
dotnet msbuild src/ExamplePlugin.Plugin/ExamplePlugin.Plugin.csproj `
  -t:BuildManagedPluginPackage `
  -p:Configuration=Release `
  -p:ManagedPluginTraceAssets=true
```

将所有实际提供私有运行时 DLL/原生文件的传递包也声明为 `ManagedPluginPrivatePackage`，再解压 ZIP 核对。
不要把 Host 共享包加入该列表。

## 4. 部署到真实 Host

已知 Host 的 `Controls` 根目录时：

```powershell
dotnet msbuild src/ExamplePlugin.Plugin/ExamplePlugin.Plugin.csproj `
  -t:DeployManagedPlugin `
  -p:Configuration=Debug `
  -p:ManagedPluginDeployRoot=C:\Path\To\Host\Controls
```

该命令只重建 `Controls/ExamplePlugin`，不会清理其他插件。另一种方式是通过 Host 提供的安装入口导入
Release ZIP。

部署或替换后完整退出并重启 Host。当前插件发现和加载上下文以进程为边界，不支持热替换。

## 5. 真实 Host 最小验收

### 加载

- 插件状态显示已加载；
- manifest 的 Plugin ID、版本和入口与项目属性一致；
- Host 没有报告共享程序集或 SDK 区间错误。

### Document

- 每个 Descriptor 都出现在正确菜单分类；
- 连续打开同一种 Document 两次得到两个标签和两个 Scope；
- 关闭一个标签只取消并释放自己的 Scope；
- 持久化 Document 可以保存、关闭、恢复；
- 恢复内容不兼容时明确失败，不猜测修复。

### Tool

- Tool 出现在 `ToolDockSide` 指定方向；
- 同一种 Tool 只有一个 Model；
- `Hide` 关闭后可以恢复且状态保留；
- `Prevent` 不允许用户关闭或隐藏。

### 更新

- 新版本保留已经发布的 Plugin、Document、Tool 稳定 ID；
- 替换插件后完整重启 Host；
- SDK 兼容区间仍包含目标 Host 提供的 SDK 版本。

## 6. 新机器常见问题

### `dotnet` 不是命令

原因：只安装了 Rider/Avalonia 插件，未安装 .NET SDK，或 Rider 在 SDK 安装前已经启动。

```powershell
winget install Microsoft.DotNet.SDK.10
```

完成后退出全部 Rider 进程并重新打开。

### 安装了 Runtime 仍不能构建

模板需要 SDK。用 `dotnet --list-sdks` 检查，而不是只看“已安装的应用”中的 Runtime。

### Rider 不识别 `.slnx`

更新到 Rider 2024.3 或更高版本。临时可以打开项目根目录，或直接打开
`src/ExamplePlugin.Standalone/ExamplePlugin.Standalone.csproj`。

### Avalonia Preview 不显示

先成功 Build Standalone，再打开 `.axaml` 并选择 **Editor and Preview**。确认 AvaloniaRider 已启用并重启
Rider。Preview 不是运行时调试；交互、键盘输入和完整资源行为仍以 Standalone 实际运行窗口为准。

### `dotnet new search` 查不到模板

模板搜索索引可能晚于普通 NuGet 包索引。直接安装精确 ID：

```powershell
dotnet new install MyAvaloniaManagement.Plugin.Templates@1.3.0
```

该命令安装公开模板。Templates `1.3.0` / SDK `3.3.0` 的锁定还原、点号名称和外部双 ALC 调用由
`scripts/Test-WorkflowActionG2.ps1` 负责；该维护门禁本身仍不执行上传。

### NuGet 还原失败

```powershell
dotnet nuget list source
dotnet restore --source https://api.nuget.org/v3/index.json
```

如果公司代理执行 TLS 检查，应由管理员正确配置代理和受信任证书，不要关闭 HTTPS 证书验证。

### Standalone 正常，Host 加载失败

这是两个不同验证层。优先检查：

1. 是否使用 `BuildManagedPluginPackage` 产生的 ZIP；
2. manifest 入口类型是否与 Module 完整类型名一致；
3. ZIP 是否误带 SDK/Avalonia/Host 共享程序集；
4. 私有依赖是否声明为 `ManagedPluginPrivatePackage`；
5. Document/Tool ID 是否属于 manifest Plugin ID；
6. 使用 Workbench Command 的插件，其 Host SDK 版本是否位于 `[3.3.0, 4.0.0)`；旧插件仍按自身 manifest 下限判断。

### Host 报 `FileNotFoundException` 或提示缺少 DLL

1. 不要复制 `bin/Release`，重新使用 `BuildManagedPluginPackage` 生成正式 ZIP；
2. 检查缺失 DLL 所属 NuGet 包是否已在 Plugin `.csproj` 声明为 `ManagedPluginPrivatePackage`；
3. 执行 `dotnet list ... package --include-transitive`，检查缺失 DLL 是否来自未声明的传递包；
4. 用 `-p:ManagedPluginTraceAssets=true` 重新打包并核对 ZIP 文件列表；
5. 整体替换旧插件目录并完整重启 Host，避免旧文件残留掩盖问题。

## 7. 常见 Host 错误方向

| 错误 | 处理方向 |
| --- | --- |
| `PLUGIN_MANIFEST_MISSING` | 使用 Build 包构建，不要手工复制 DLL |
| `PLUGIN_MANIFEST_INVALID` | 删除手写 manifest，检查项目中的 ManagedPlugin 属性 |
| `PLUGIN_SDK_INCOMPATIBLE` | 用目标 Host 对应 SDK 重新编译，检查 SDK 区间 |
| `PLUGIN_ENTRY_INVALID` | Module 必须 public、非抽象、非泛型、有 public 无参构造并实现 `IPluginModule` |
| `PLUGIN_SHARED_ASSEMBLY_MISMATCH` | 从插件 ZIP 删除 SDK、Avalonia、Dock 和其他 Host 共享程序集 |
| `DOCUMENT_ID_OWNER_MISMATCH` | 使用 `{PluginId}.document.*` |
| `TOOL_ID_OWNER_MISMATCH` | 使用 `{PluginId}.tool.*` |
| `PLUGIN_CONTRIBUTION_SERVICE_REGISTRATION_FORBIDDEN` | 不要在 Module 中重复手工登记 Document/Tool 根类型 |
| `VIEW_CREATION_FAILED` | 检查 AXAML、资源和 View 的 public 无参构造 |

## 8. 不可越过的边界

1. manifest 是插件身份唯一事实源，由 Build 包生成。
2. Module 只在组合阶段同步登记贡献，不保存 `IPluginRegistration`。
3. Document Scope 由 Host 拥有；插件只观察 `IDocumentLifetime`。
4. Tool Model 是插件级 singleton；隐藏不等于释放。
5. Stable ID 不跟随显示名、类名和文件夹重命名。
6. Standalone Stub 不能进入 Plugin 项目或插件 ZIP。
7. 当前只支持 Windows x64 插件交付，不支持热更新。

更完整的包和版本规则见
[外部 Managed Plugin 开发、模板与 NuGet 发布指南](../design/external-managed-plugin-development-and-installation-plan.md)。
