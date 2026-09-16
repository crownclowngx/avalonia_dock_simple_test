# V9：Avalonia / Dock 升级使用与开发验证

版本事实以 [Directory.Version.props](../../Directory.Version.props) 为准；逐阶段证据见 [专用开发验收](../plan-history/host-v9/development-acceptance.md)。

## 升级后的边界

- Host：Avalonia 12.1.2、Dock 12.1.0.6，产品版本仍为 3.0.0。
- 六个自有 NuGet 包统一为 3.4.1，发布状态与公共源消费证据见 [统一发布记录](../plan-history/host-v9/nuget-unified-3.4.1-release.md)。Host 安装目录没有部署。
- SDK public API、manifest schema 2、Document schema 2、layout-v2.json 均保持兼容，不迁移用户数据。
- Semi 12.1.0、Ursa 2.1.0 保持原版本。Workflow 包号对齐 3.4.1，保留 v1 API 和 AssemblyVersion 1.0.0.0。

## 其他插件需要升级吗

**现有、按 SDK 3.4.0 编译的插件可以先保留原 DLL、版本和 manifest。** Host 为插件提供共享 Avalonia/UI SDK；本次测试的 12 个插件没有直接引用 Dock。它们不是各自运行一份完整 UI 框架，因此不需要机械同步全部插件版本。

但共享运行时只解决程序集身份，并不能保证每个业务流程都兼容。此次逐个验证了 12 个冻结产物的加载、DI 组合以及全部声明视图，另对部分插件验证了真实 Workspace 链路。视频播放、账号登录、下载、数据库迁移、定时任务和真实 Windows 输入仍需对应业务验收；具体覆盖范围以记录为准。

| 场景 | 处理 |
| --- | --- |
| 继续运行已有 SDK 3.4.0 二进制 | 保留包和清单，在新版 Host 回归 |
| 新项目或决定重新编译插件 | Core/UI 一起使用 3.4.1，更新锁文件；新插件最低 SDK 为 3.4.1 |
| 插件 Standalone 独立运行 | 它自行提供 UI 运行时；重新编译时将 Avalonia.Desktop 对齐 12.1.2 |
| 插件直接引用 Dock、反射 Host internal API 或捆绑共享 DLL | 另做兼容检查，不在本次“可原样保留”的已验证集合内 |

不要手动把新编译插件的最低 SDK 降回 3.4.0；也不要覆盖公开的同版本 SDK 包。构建输出和本地包候选不等于用户安装目录。

## Dock 定制的最终处置

| 定制 | 决策 |
| --- | --- |
| App.axaml 的两个浮动窗口样式 | 删除；Host 已禁止浮动，主窗口原生标题栏继续保留 |
| DockTabPointerCaptureGuard | 保留并改造；正确订阅 Direct 捕获丢失事件，移交后停止恢复接收方状态 |
| DocumentControlRecycling | 保留并改造；安全解绑真实/逻辑父级，保留绑定，未知父级明确失败，最终关闭保证释放 |
| HostDockFactory / DockDocumentLifetime | 保留职责和回调协议，补充取消、异常、最后标签、重复初始化测试 |
| Layout V2 映射 / 工具停靠协调器 | 保留；四向分割、隐藏、固定、恢复和视图所有权回归继续覆盖 |

## 使用统一版本模板

```powershell
dotnet new install MyAvaloniaManagement.Plugin.Templates@3.4.1
dotnet new myavalonia-plugin -n ExamplePlugin --plugin-id myavalonia.plugin.example
cd ExamplePlugin
dotnet restore --locked-mode
dotnet build -c Release --no-restore -warnaserror
dotnet test -c Release --no-build --no-restore
dotnet msbuild src/ExamplePlugin.Plugin/ExamplePlugin.Plugin.csproj -t:BuildManagedPluginPackage -p:Configuration=Release
```

模板直接依赖的 Core/UI SDK、Icons、Build 都精确固定到 `[3.4.1]`，三个 lock file 使用公共源最终包哈希。Workflow SDK 为可选包，需要时同样引用 `3.4.1`。生成插件的业务版本仍为 `1.0.0`，不能把 SDK 的发布版本误当作每个业务插件的版本。

开发下一版本时，先集中修改 `MyAvaloniaPackageVersion`，再同步模板的精确依赖和最低 SDK。基础包发布后需从公共源重新生成模板锁文件，最后发布模板；不要将本地未签名 nupkg 的 contentHash 用于公开模板。发布步骤见 [专用记录](../plan-history/host-v9/nuget-unified-3.4.1-release.md)。

## 开发门禁和外部产物验证

```powershell
# 本地开发 verify：构建、SDK/Host/插件/UI 测试、架构契约和测试 ZIP 验收。
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify

# 外部验收独立构建，不修改默认测试集合。路径须指向保留完整私有依赖的 Controls 副本。
$env:MYAVALONIA_EXTERNAL_CONTROLS = (Resolve-Path artifacts/host-v9/baseline/old-controls).Path
$env:MYAVALONIA_DATA_DIRECTORY = Join-Path (Get-Location) 'artifacts/host-v9/external-runtime'
dotnet test Host/MyAvaloniaManagement.UiTests -c Release -warnaserror -p:HostExternalPluginAcceptance=true --artifacts-path artifacts/host-v9/external-build --filter FullyQualifiedName~ExternalPluginBinaryAcceptanceTests

# 核查过无外部业务副作用后，再显式选取插件运行模型初始化与真实 Dock 链路。
$env:MYAVALONIA_WORKSPACE_PLUGIN_IDS = 'myavalonia.plugin.my-plug-test,myavalonia.plugin.classic.game'
dotnet test Host/MyAvaloniaManagement.UiTests -c Release -warnaserror -p:HostExternalPluginAcceptance=true --artifacts-path artifacts/host-v9/external-build --filter FullyQualifiedName~ExternalPluginWorkspaceAcceptanceTests
Remove-Item Env:MYAVALONIA_EXTERNAL_CONTROLS, Env:MYAVALONIA_WORKSPACE_PLUGIN_IDS, Env:MYAVALONIA_DATA_DIRECTORY
```

外部产物测试明确缺输入即失败，不把空目录或缺少旧包当作通过。默认 verify 不编译这些需要外部输入的夹具；不通过跳过测试降低门槛。

## Host 安装程序发布前仍需完成

实际 Windows 标签跨区拖拽、Esc/失活/移出窗口、多显示器 DPI、原生视频和各插件真实业务任务；然后按当时的发布要求执行 Windows/发布门禁和完整备份回退演练。本轮不执行这些发布流程。回退时恢复同一套 Host/UI 依赖，保持旧插件产物和 schema 2 数据；不要只回退单个 Dock DLL。
