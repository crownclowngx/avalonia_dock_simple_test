# 从只有 Rider 和 Avalonia 的机器创建插件

本篇假设一台 Windows x64 开发机只安装了 JetBrains Rider，以及 Rider 中的 Avalonia/AvaloniaRider
插件。目标是不克隆 Host 源码，直接从 NuGet.org 安装模板并创建一个能独立运行、调试、测试和打包的
真实插件项目。

## 1. 先确认 .NET 10 SDK

Rider 是 IDE，AvaloniaRider 是 XAML 编辑/预览扩展；它们都不能代替 .NET SDK。当前模板目标框架是
`net10.0`，所以必须安装 **.NET 10 SDK**，只有 Runtime 不够。

在 PowerShell 或 Rider Terminal 中执行：

```powershell
dotnet --version
dotnet --list-sdks
```

输出中必须有 `10.0.x`。如果 `dotnet` 不存在，或者列表中没有 10.0 SDK，在管理员 PowerShell 中安装：

```powershell
winget install Microsoft.DotNet.SDK.10
```

安装后完全退出并重新启动 Rider，再次执行检查。也可以按
[Microsoft 的 Windows 安装说明](https://learn.microsoft.com/dotnet/core/install/windows)下载安装器。

> 安装 SDK 会同时提供对应 Runtime，不需要再单独安装 `.NET Desktop Runtime 10`。

## 2. Rider 与 Avalonia 插件

建议更新到当前 Rider。Rider 从 2024.3 开始支持 `.slnx`；当前版本可以直接打开模板生成的
`ExamplePlugin.slnx`。相关操作见
[Rider 的 SLNX 说明](https://www.jetbrains.com/help/rider/Extending_Your_Solution.html#slnx)。如果旧版 Rider
不能识别 `.slnx`，优先更新 Rider；临时也可以直接打开项目根目录或 Standalone `.csproj`。

AvaloniaRider 只改善 AXAML 编辑和预览，不负责安装本模板或 NuGet 依赖。在 Rider 中可检查：

```text
Settings → Plugins → Marketplace → AvaloniaRider
```

安装或更新后重启 Rider。官方步骤见
[Avalonia IDE 配置](https://docs.avaloniaui.net/docs/get-started/set-up-your-ide)。模板已经引用经过验证的
Avalonia 12 包，不需要另外安装 `Avalonia.Templates` 才能创建本插件。

## 3. 安装 Managed Plugin 模板

在准备存放源码的目录打开 PowerShell：

```powershell
dotnet new install MyAvaloniaManagement.Plugin.Templates@1.2.0
dotnet new list myavalonia
dotnet new myavalonia-plugin --help
```

公开模板 `1.2.0` 精确使用 Core/UI `3.2.0` 与 Build `1.1.2`，并为三个生成项目携带 lock file。

应能看到短名称 `myavalonia-plugin`。若想搜索公开包：

```powershell
dotnet package search MyAvaloniaManagement `
  --source https://api.nuget.org/v3/index.json
```

NuGet 的普通包索引和模板搜索索引不是同一个索引。刚发布时 `dotnet new search myavalonia-plugin` 可能
暂时查不到，但按精确包 ID 安装仍然有效。

## 4. 创建真实插件解决方案

选择一个发布后不会再改变的插件身份：

```powershell
dotnet new myavalonia-plugin `
  -n ExamplePlugin `
  --plugin-id myavalonia.plugin.example
```

规则：

- 项目名 `ExamplePlugin` 可以以后重构或更换显示名称；
- `myavalonia.plugin.example` 是持久身份，发布后不要随意改变；
- 只使用小写字母、数字、点和连字符；
- Document ID 使用 `{PluginId}.document.*`；
- Tool ID 使用 `{PluginId}.tool.*`。

生成结果：

```text
ExamplePlugin/
├─ ExamplePlugin.slnx
├─ Directory.Build.props
├─ Directory.Packages.props
├─ src/
│  ├─ ExamplePlugin.Plugin/
│  │  ├─ Constants/PluginIds.cs
│  │  ├─ Features/Main/
│  │  └─ Plugin/ExamplePluginModule.cs
│  └─ ExamplePlugin.Standalone/
├─ tests/
│  └─ ExamplePlugin.Tests/
└─ docs/
   ├─ README.md
   ├─ project-and-window-responsibilities.md
   └─ deployment-and-release.md
```

三个项目的职责：

| 项目 | 职责 | 是否进入插件 ZIP |
| --- | --- | --- |
| `ExamplePlugin.Plugin` | View、ViewModel、Module、业务服务 | 是 |
| `ExamplePlugin.Standalone` | 独立 Avalonia 调试窗口 | 否 |
| `ExamplePlugin.Tests` | 插件业务和注册测试 | 否 |

默认不要创建只有转发作用的 Core。只有同一业务必须被多个插件、命令行或服务共同复用时才提取 Core，
并把 Core 明确声明为插件私有运行时资产。

## 5. 第一次命令行构建

```powershell
cd ExamplePlugin
dotnet restore
dotnet build -c Debug -warnaserror
dotnet test -c Debug --no-build
```

模板使用 NuGet.org 上的精确版本：

- `MyAvaloniaManagement.PluginSdk` `3.1.0`；
- `MyAvaloniaManagement.PluginSdk.UI` `3.1.0`；
- `MyAvaloniaManagement.Plugin.Build` `1.1.2`；
- Avalonia `12.x` 模板锁定版本。

首次还原需要下载 Avalonia 和测试依赖，可能比后续构建慢。公司代理或自定义 NuGet 源下失败时，先用：

```powershell
dotnet nuget list source
dotnet restore --source https://api.nuget.org/v3/index.json
```

### 新增 NuGet 运行时包时同步三个位置

模板启用了中央包版本管理。假设 Plugin 业务新增 `Some.Private.Runtime`，必须同时修改：

```xml
<!-- 1. 解决方案根 Directory.Packages.props -->
<PackageVersion Include="Some.Private.Runtime" Version="[1.2.3]" />

<!-- 2、3. src/ExamplePlugin.Plugin/ExamplePlugin.Plugin.csproj -->
<PackageReference Include="Some.Private.Runtime" />
<ManagedPluginPrivatePackage Include="Some.Private.Runtime" />
```

第三行声明该包的运行时 DLL/当前 win-x64 原生资产属于插件并必须进入正式 ZIP。只写前两处时，普通构建或
Standalone 可能因为 `bin` 中有 CopyLocal 文件而正常，但正式 ZIP 会缺少 DLL。若 NuGet 包还有提供运行时
文件的传递依赖，用下面的命令查看，并把相应传递包 ID 也逐一加入 `ManagedPluginPrivatePackage`：

```powershell
dotnet list src/ExamplePlugin.Plugin/ExamplePlugin.Plugin.csproj package --include-transitive
```

不要把 Host 共享的 Plugin SDK、Avalonia、Dock、Semi、Ursa、CommunityToolkit、
`Microsoft.Extensions.*` 或 Newtonsoft.Json 标为私有。只给 Standalone/Tests 使用的依赖应加在相应项目，
不放进 Plugin ZIP。原生目录和额外文件的声明方式见模板生成的 `docs/deployment-and-release.md`。

## 6. 在 Rider 打开与调试

1. Rider 欢迎页选择 **Open**。
2. 打开 `ExamplePlugin/ExamplePlugin.slnx`。
3. 等待右下角 NuGet Restore 和项目索引完成。
4. 打开 **Run → Edit Configurations**，新增 **.NET Project** 配置。
5. Project 选择 `src/ExamplePlugin.Standalone/ExamplePlugin.Standalone.csproj`。
6. 在 `MainDocument.InitializeAsync`、命令或业务服务中设置断点。
7. 选择刚建立的配置并点击 Debug。

也可以在 Rider Terminal 中启动：

```powershell
dotnet run --project src/ExamplePlugin.Standalone
```

Standalone 通过 `ProjectReference` 直接使用同一个 Plugin 项目，因此 Plugin 中的断点会正常命中。修改
AXAML 后先重新 Build，再在编辑器中选择 **Editor and Preview**；Avalonia 官方也要求预览器先有一个
成功构建的可执行目标。

## 7. 当前 Standalone 能验证什么

模板 `1.1.0` 默认直接创建 `MainDocument` 并显示 `MainView`，适合验证：

- AXAML 布局和主题资源；
- 编译绑定；
- ViewModel 命令和状态；
- 插件自己的业务服务；
- Rider 断点与异常。

它不会加载 ZIP、读取 manifest、创建真实 Dock 或模拟全部 Host Port。新增多个 Document/Tool 后如何扩展
Standalone，见[添加多个 Document、Tool 和独立预览工作台](./add-document-and-tool.md)。

## 8. 生成真实插件 ZIP

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

Build 包会在隔离目录执行锁定还原、Release 构建、manifest/程序集/资产校验、确定性 ZIP 和重新解压哈希
验证。不要手写或复制 `plugin.manifest.json`。

## 9. 部署到真实 Host

在只有 Rider 的机器上可以完成创建、独立调试、测试和 ZIP 打包；要验证真实插件加载，仍然必须取得
Host 可执行程序或由 Host 维护者提供的测试安装目录。无需取得 Host 源码。

已知 Host `Controls` 目录时可以显式部署：

```powershell
dotnet msbuild src/ExamplePlugin.Plugin/ExamplePlugin.Plugin.csproj `
  -t:DeployManagedPlugin `
  -p:Configuration=Debug `
  -p:ManagedPluginDeployRoot=C:\Path\To\Host\Controls
```

也可以把 Release ZIP 交给 Host 的安装流程。替换插件文件后必须完整退出并重启 Host；当前不支持热更新。

下一步：[添加多个 Document、Tool 和独立预览工作台](./add-document-and-tool.md)。
