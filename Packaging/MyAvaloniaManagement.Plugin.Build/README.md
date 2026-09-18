# MyAvaloniaManagement Plugin Build

> 用途：当前包消费说明；核对日期：2026-09-16。事实源为本包项目、公开类型或随包构建/模板内容。

`3.4.1` 延续 1.1.3 的兼容协议，精确允许 `MyAvaloniaManagement.Icons.dll` 作为普通私有图标资源部署及打包。
Host 主程序集、Core/UI/Workflow SDK 和其他既有共享程序集仍受原规则保护。
使用图标包时同时添加 `PackageReference` 和 `ManagedPluginPrivatePackage`，资源包不会自动加入共享契约。

本包是 Managed Plugin 的开发期构建协议。插件项目直接引用本包并设置 `PrivateAssets="all"`；
包不会进入插件运行目录，也不会向仓库外项目猜测 Host 安装位置。

普通 `dotnet build` 会校验插件声明和入口，并生成 `plugin.manifest.json`。显式提供
`ManagedPluginDeployRoot` 时才部署插件目录；调用 `BuildManagedPluginPackage` 会在隔离目录中执行
锁定还原和 Release 构建，最终生成确定性 ZIP 与外置摘要清单。

V10 工作树增量（尚未公开发布）：构建同时生成可选 `plugin.build.json`，包含目标框架、实际解析的
NuGet 包版本和 Build 版本。此文件随部署和 ZIP 一起交付，不修改严格 manifest；不要手动填写或加入时间、
个人路径。旧产物没有该文件时仍可加载，其编译包版本显示未知。程序集引用版本与 NuGet 包版本分别展示。
候选消费验证使用隔离版本和 feed，不覆盖已经发布的 3.4.1。

小镇部署修正的本地候选 `3.4.2-richtown.1` 精确允许 `Microsoft.Extensions.DependencyModel.dll`，
保留 DI、Configuration 及所有其他既有共享/禁带保护。该组件不属于 Host 的实际共享闭包；由声明其依赖的插件携带，
运行时仍走现有插件 ALC/deps 解析，不增加全局解析器或改变 Host 共享实例。此候选只在本地 feed 消费，不代表已上传 NuGet.org。

兼容验收报告放在插件目录之外，由 Host 维护者的本地工具生成。在“工具 → 插件看板…”导入，
再显式检查安装产物；加载成功不能代表 Workspace、原生播放或业务流程已经验证。

## 新增 NuGet 运行时依赖

插件依赖一个新的 NuGet 包时，只有 `PackageReference` 还不够。Build 包故意只把插件显式拥有的私有
运行时资产放进部署目录和 ZIP，避免把 Host 共享程序集误打包。使用中央版本管理的模板需要同时修改：

1. 在解决方案根 `Directory.Packages.props` 增加 `PackageVersion`；
2. 在 `src/<插件名>.Plugin/<插件名>.Plugin.csproj` 增加 `PackageReference`；
3. 在同一个 Plugin 项目中增加 `ManagedPluginPrivatePackage`，其 `Include` 必须是准确的 NuGet 包 ID。

```xml
<!-- Directory.Packages.props -->
<PackageVersion Include="Some.Private.Runtime" Version="[1.2.3]" />

<!-- src/<插件名>.Plugin/<插件名>.Plugin.csproj -->
<PackageReference Include="Some.Private.Runtime" />
<ManagedPluginPrivatePackage Include="Some.Private.Runtime" />
```

若该包还有提供运行时 DLL 或原生文件的传递依赖，也要把这些传递包的准确 ID 逐一声明为
`ManagedPluginPrivatePackage`。可用 `dotnet list <Plugin.csproj> package --include-transitive` 查看依赖树。
SDK、Avalonia、Dock、Semi、Ursa、CommunityToolkit、`Microsoft.Extensions.*` 和 Newtonsoft.Json 受禁带规则保护，
除上述精确的 DependencyModel 私有例外外，不得声明为插件私有包。禁带不等于 Host 保证提供：Newtonsoft.Json 是历史限制，Microsoft.Extensions 仅提供
明确根及其实际依赖闭包。V10 开发中的规则统一来自 `MyAvaloniaManagement.RuntimeProfile.props`，尚未公开发布。
只被 Standalone 或 Tests 使用的包只加到对应项目，不进入插件 ZIP。

如果漏掉第 3 步，普通 `bin` 或 Standalone 可能仍能运行，但 Build 生成的正式 ZIP 不会携带该 DLL，部署后
会出现 `FileNotFoundException`、`FileLoadException` 或类型初始化失败。发布前务必解压 ZIP 检查私有 DLL，
再在真实 Host 中完成冷启动验收。

```powershell
dotnet build
dotnet msbuild -t:BuildManagedPluginPackage -p:Configuration=Release
dotnet msbuild -t:DeployManagedPlugin -p:ManagedPluginDeployRoot=C:\Path\To\Controls
```
