# 外部 Managed Plugin 开发、模板与 NuGet 发布指南

> 当前基线：Plugin SDK V3、manifest schema 2、.NET 10、Avalonia 12、Windows x64
> 更新时间：2026-08-24
> 本文替代旧的 Host v1 候选计划。旧文档中的 manifest v1、Legacy 入口、双 Host/Common 区间和
> `Package::Version` 模板安装语法均已失效。

## 1. 当前交付物

外部插件开发由四个 NuGet 包组成：

| 包 | 版本 | 职责 | 进入插件 ZIP |
| --- | --- | --- | --- |
| `MyAvaloniaManagement.PluginSdk` | `3.0.0` | 平台无关身份、Document、内容、关闭与生命周期契约 | 否，Host 提供 |
| `MyAvaloniaManagement.PluginSdk.UI` | `3.0.0` | Avalonia 模块入口、DI、Document/Tool/View 和窗口端口 | 否，Host 提供 |
| `MyAvaloniaManagement.Plugin.Build` | `1.0.0` | 声明校验、manifest、资产部署和确定性 ZIP | 否，仅开发期 |
| `MyAvaloniaManagement.Plugin.Templates` | `1.0.1` | `dotnet new myavalonia-plugin` 解决方案模板与项目内置文档 | 否，仅创建时 |

以上四个版本已于 2026-08-24 发布到 NuGet.org：
[Core SDK](https://www.nuget.org/packages/MyAvaloniaManagement.PluginSdk/3.0.0)、
[UI SDK](https://www.nuget.org/packages/MyAvaloniaManagement.PluginSdk.UI/3.0.0)、
[Build](https://www.nuget.org/packages/MyAvaloniaManagement.Plugin.Build/1.0.0) 和
[Templates](https://www.nuget.org/packages/MyAvaloniaManagement.Plugin.Templates/1.0.1)。

Core/UI SDK 同版本发布。Build 与 Templates 独立演进；模板固定一组经过端到端验证的精确版本，避免
外部插件还原到当前 Host 未验证的共享程序集组合。

## 2. 模板生成的结构

现有四个真实插件都把 View、ViewModel、PluginModule 与业务实现放在一个插件项目中。模板沿用这一
真实边界，不默认创建名义上的 Core 项目：

```text
ExamplePlugin/
├─ ExamplePlugin.slnx
├─ Directory.Build.props
├─ Directory.Packages.props
├─ src/
│  ├─ ExamplePlugin.Plugin/       # 唯一真实插件程序集
│  └─ ExamplePlugin.Standalone/   # Avalonia 独立预览程序
└─ tests/
   └─ ExamplePlugin.Tests/
```

真实交付物只有 `ExamplePlugin.Plugin.dll`。Standalone 和 Tests 都引用同一个 Plugin 项目。只有业务
逻辑需要被多个插件、命令行或服务共同消费，或者要独立发布 NuGet 时，才提取 `ExamplePlugin.Core`。
拆分后 Core DLL 必须作为明确的插件私有资产进入 ZIP，不能依赖偶然复制。

## 3. 搜索、安装与创建模板

从 NuGet.org 安装：

```powershell
dotnet new install MyAvaloniaManagement.Plugin.Templates@1.0.1
dotnet new list myavalonia
dotnet new myavalonia-plugin --help
```

.NET 9 以后版本分隔符使用 `@`；旧的 `Package::Version` 已弃用。搜索模板：

```powershell
dotnet new search myavalonia-plugin
```

创建项目：

```powershell
dotnet new myavalonia-plugin `
  -n ExamplePlugin `
  --plugin-id myavalonia.plugin.example
```

`--plugin-id` 是持久身份，不是显示名称。发布后不要随类名或产品名称改变。Document/Tool ID 必须分别
位于 `{PluginId}.document.*` 和 `{PluginId}.tool.*`。

恢复、构建、测试与独立预览：

```powershell
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet run --project src/ExamplePlugin.Standalone
```

Standalone 不是第二套 Host，只复用插件的 View、ViewModel 与业务服务。manifest、加载上下文、
Document Scope、Dock、Tool、Host Port 和生命周期必须通过真实 Host 做最终验收。

## 4. 插件项目声明

```xml
<PropertyGroup>
  <ManagedPlugin>true</ManagedPlugin>
  <ManagedPluginId>myavalonia.plugin.example</ManagedPluginId>
  <ManagedPluginDirectoryName>ExamplePlugin</ManagedPluginDirectoryName>
  <PluginVersion>1.0.0</PluginVersion>
  <ManagedPluginRuntimeIdentifier>win-x64</ManagedPluginRuntimeIdentifier>
  <ManagedPluginEntryType>ExamplePlugin.Plugin.ExamplePluginModule</ManagedPluginEntryType>
  <ManagedPluginSdkMinInclusive>3.0.0</ManagedPluginSdkMinInclusive>
  <ManagedPluginSdkMaxExclusive>4.0.0</ManagedPluginSdkMaxExclusive>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="MyAvaloniaManagement.PluginSdk" Version="[3.0.0]" />
  <PackageReference Include="MyAvaloniaManagement.PluginSdk.UI" Version="[3.0.0]" />
  <PackageReference Include="MyAvaloniaManagement.Plugin.Build"
                    Version="[1.0.0]"
                    PrivateAssets="all" />
</ItemGroup>
```

Build 包是开发依赖。Plugin SDK、Avalonia、Dock、Semi、Ursa、CommunityToolkit 与
`Microsoft.Extensions.*` 由 Host 共享，不能复制进插件 ZIP。

插件自己的运行时依赖必须显式声明所有权：

```xml
<ItemGroup>
  <PackageReference Include="Some.Private.Runtime" Version="1.2.3" />
  <ManagedPluginPrivatePackage Include="Some.Private.Runtime" />
</ItemGroup>
```

原生目录或其他文件分别使用 `ManagedPluginAssetDirectoryRelativePath` 和 `ManagedPluginAsset`；目标必须
位于插件目录内。当前只接受 `win-x64`。

## 5. Build 包的使用

普通构建：

```powershell
dotnet build src/ExamplePlugin.Plugin/ExamplePlugin.Plugin.csproj
```

它会校验身份、版本、入口、RID 和 SDK 区间，使用独立 C# 探针验证 `IPluginModule`，并生成 DLL、deps、
PDB 和严格 `plugin.manifest.json`。外部项目不会猜测 Host 位置，也不会默认部署。

显式部署给真实 Host：

```powershell
dotnet msbuild src/ExamplePlugin.Plugin/ExamplePlugin.Plugin.csproj `
  -t:DeployManagedPlugin `
  -p:Configuration=Debug `
  -p:ManagedPluginDeployRoot=C:\Path\To\Host\Controls
```

只重建当前插件目录，不清理 Controls 根或兄弟插件。替换后必须完整重启 Host。

生成独立 ZIP：

```powershell
dotnet msbuild src/ExamplePlugin.Plugin/ExamplePlugin.Plugin.csproj `
  -t:BuildManagedPluginPackage `
  -p:Configuration=Release
```

默认输出：

```text
src/ExamplePlugin.Plugin/artifacts/managed-plugin-packages/
├─ ExamplePlugin.Plugin-1.0.0-win-x64.zip
└─ ExamplePlugin.Plugin-1.0.0-win-x64.manifest.json
```

打包目标读取 MSBuild 最终求值结果，不解析项目 XML，也不依赖本仓库路径。它执行锁定还原、隔离构建、
最终 manifest/程序集/资产校验，按稳定顺序和固定时间戳生成 ZIP，再重新解压并核对长度与 SHA-256。

## 6. 模板如何维护

模板源码位于：

```text
Packaging/MyAvaloniaManagement.Plugin.Templates/
├─ MyAvaloniaManagement.Plugin.Templates.csproj
├─ README.md
└─ content/myavalonia-plugin/
   ├─ .template.config/template.json
   ├─ DemoPlugin.slnx
   ├─ Directory.Build.props
   ├─ Directory.Packages.props
   ├─ docs/
   ├─ src/
   └─ tests/
```

`sourceName: DemoPlugin` 替换解决方案、项目、命名空间和文件名；`--plugin-id` 替换稳定身份占位符。
模板源码本身必须是可编译的普通解决方案，不在 C# 中放模板专用语法。

本地打包和安装：

```powershell
dotnet pack Packaging/MyAvaloniaManagement.Plugin.Templates/MyAvaloniaManagement.Plugin.Templates.csproj `
  -c Release -o artifacts/nuget
dotnet new uninstall MyAvaloniaManagement.Plugin.Templates
dotnet new install .\artifacts\nuget\MyAvaloniaManagement.Plugin.Templates.1.0.1.nupkg
```

每次模板变更至少验证：本地四包 → 安装模板 → 系统临时目录创建插件 → 隔离还原 → Plugin、Standalone、
Tests 零警告构建 → 测试 → `BuildManagedPluginPackage` → 最终 ZIP/manifest 检查。

## 7. Gitee 项目发布 NuGet

### 7.1 不需要 GitHub 地址

普通 API Key 发布不要求 GitHub。包元数据可以直接指向 Gitee：

```xml
<RepositoryType>git</RepositoryType>
<RepositoryUrl>https://gitee.com/crownclowngx/avalonia_dock_simple_test.git</RepositoryUrl>
<PackageProjectUrl>https://gitee.com/crownclowngx/avalonia_dock_simple_test</PackageProjectUrl>
```

Trusted Publishing 页面要求 GitHub/GitLab，是因为它使用这些平台的 OIDC 临时身份；它不是普通发布的
强制入口。只使用 Gitee 时创建受限 API Key，不要填写虚假 GitHub 地址。

在 NuGet.org 的 `API Keys -> Create` 设置：

- Scope：`Push new packages and package versions`；
- Glob Pattern：`MyAvaloniaManagement.*`；
- 使用短有效期；
- Key 只进入当前进程环境变量或 CI Secret，不写入仓库、脚本或文档。

```powershell
$env:NUGET_API_KEY = Read-Host -Prompt "NuGet API Key" -MaskInput
```

### 7.2 发布前构建

```powershell
dotnet restore --locked-mode
.\scripts\Test-PluginSdkPackage.ps1 -Configuration Release

dotnet pack Host/MyAvaloniaManagement.PluginSdk/MyAvaloniaManagement.PluginSdk.csproj `
  -c Release --no-restore -o artifacts/nuget
dotnet pack Host/MyAvaloniaManagement.PluginSdk.UI/MyAvaloniaManagement.PluginSdk.UI.csproj `
  -c Release --no-restore -o artifacts/nuget
dotnet pack Packaging/MyAvaloniaManagement.Plugin.Build/MyAvaloniaManagement.Plugin.Build.csproj `
  -c Release --no-restore -o artifacts/nuget
dotnet pack Packaging/MyAvaloniaManagement.Plugin.Templates/MyAvaloniaManagement.Plugin.Templates.csproj `
  -c Release --no-restore -o artifacts/nuget
```

按 Core SDK、UI SDK、Build、Templates 的顺序推送：

```powershell
dotnet nuget push .\artifacts\nuget\MyAvaloniaManagement.PluginSdk.3.0.0.nupkg `
  --source https://api.nuget.org/v3/index.json
dotnet nuget push .\artifacts\nuget\MyAvaloniaManagement.PluginSdk.UI.3.0.0.nupkg `
  --source https://api.nuget.org/v3/index.json
dotnet nuget push .\artifacts\nuget\MyAvaloniaManagement.Plugin.Build.1.0.0.nupkg `
  --source https://api.nuget.org/v3/index.json
dotnet nuget push .\artifacts\nuget\MyAvaloniaManagement.Plugin.Templates.1.0.1.nupkg `
  --source https://api.nuget.org/v3/index.json
Remove-Item Env:NUGET_API_KEY
```

SDK 的 `.snupkg` 推送到同一 V3 源。公开包的同一 ID + Version 不可覆盖；发现问题只能提升版本，或把
错误版本 Unlist。当 `.nupkg` 与同名 `.snupkg` 位于同一输出目录时，当前 `dotnet nuget push` 会在推送
主包时一并提交符号包，不要再重复推送同一个 `.snupkg`。

## 8. 发布后搜索与最终验收

NuGet 验证和索引可能稍有延迟。精确页面：

```text
https://www.nuget.org/packages/MyAvaloniaManagement.PluginSdk
https://www.nuget.org/packages/MyAvaloniaManagement.PluginSdk.UI
https://www.nuget.org/packages/MyAvaloniaManagement.Plugin.Build
https://www.nuget.org/packages/MyAvaloniaManagement.Plugin.Templates
```

CLI 搜索：

```powershell
dotnet package search MyAvaloniaManagement --source https://api.nuget.org/v3/index.json
dotnet new search myavalonia-plugin
```

NuGet 包搜索、包页面和模板目录使用不同索引，刚发布时 `dotnet package search` 可能已经可见，而
`dotnet new search` 暂时还查不到。此时可以直接使用精确包 ID 安装；安装成功即不需要等待模板搜索索引。

公共源最终验收：

```powershell
dotnet new install MyAvaloniaManagement.Plugin.Templates@1.0.1
dotnet new myavalonia-plugin -n PublicFeedProbe --plugin-id myavalonia.plugin.public-feed-probe
cd PublicFeedProbe
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet msbuild src/PublicFeedProbe.Plugin/PublicFeedProbe.Plugin.csproj `
  -t:BuildManagedPluginPackage -p:Configuration=Release
```

随后把 ZIP 部署到真实 Host，完成 Loader、Registry、Workspace、Dock 和生命周期验收。

## 9. 兼容与安全边界

- SDK 破坏性变化提升主版本并建立新的 PublicAPI 基线；
- 兼容新增可以提升次版本，但模板必须同步最低 SDK 区间；
- Build/Templates 修复独立提升自身版本，公开版本不可重打；
- manifest、Document envelope、layout、产品和 SDK 版本是独立事实；
- 插件在 Host 进程内拥有与 Host 相同的操作系统权限，不是安全沙箱；
- 当前不支持热更新、热卸载、在线市场、自动安装或发布者签名；
- NuGet API Key 一旦进入聊天、日志或代码应立即撤销；
- NuGet.org 是公开源，不应公开的 SDK 必须使用私有 NuGet 源。

仓库当前没有 LICENSE 文件。公开发布不等于授予开源许可；长期维护前必须由所有者明确选择开源许可
证或提供自定义许可证文件，不能由构建脚本或模板代替法律决策。
