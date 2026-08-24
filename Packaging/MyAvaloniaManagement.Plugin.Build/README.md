# MyAvaloniaManagement Plugin Build

本包是 Managed Plugin 的开发期构建协议。插件项目直接引用本包并设置 `PrivateAssets="all"`；
包不会进入插件运行目录，也不会向仓库外项目猜测 Host 安装位置。

普通 `dotnet build` 会校验插件声明和入口，并生成 `plugin.manifest.json`。显式提供
`ManagedPluginDeployRoot` 时才部署插件目录；调用 `BuildManagedPluginPackage` 会在隔离目录中执行
锁定还原和 Release 构建，最终生成确定性 ZIP 与外置摘要清单。

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
SDK、Avalonia、Dock、Semi、Ursa、CommunityToolkit、`Microsoft.Extensions.*` 和 Newtonsoft.Json 由当前
Host 共享，不得声明为插件私有包。只被 Standalone 或 Tests 使用的包则只加到对应项目，不进入插件 ZIP。

如果漏掉第 3 步，普通 `bin` 或 Standalone 可能仍能运行，但 Build 生成的正式 ZIP 不会携带该 DLL，部署后
会出现 `FileNotFoundException`、`FileLoadException` 或类型初始化失败。发布前务必解压 ZIP 检查私有 DLL，
再在真实 Host 中完成冷启动验收。

```powershell
dotnet build
dotnet msbuild -t:BuildManagedPluginPackage -p:Configuration=Release
dotnet msbuild -t:DeployManagedPlugin -p:ManagedPluginDeployRoot=C:\Path\To\Controls
```
