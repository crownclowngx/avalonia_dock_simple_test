# MyAvaloniaManagement Plugin Build

本包是 Managed Plugin 的开发期构建协议。插件项目直接引用本包并设置 `PrivateAssets="all"`；
包不会进入插件运行目录，也不会向仓库外项目猜测 Host 安装位置。

普通 `dotnet build` 会校验插件声明和入口，并生成 `plugin.manifest.json`。显式提供
`ManagedPluginDeployRoot` 时才部署插件目录；调用 `BuildManagedPluginPackage` 会在隔离目录中执行
锁定还原和 Release 构建，最终生成确定性 ZIP 与外置摘要清单。

```powershell
dotnet build
dotnet msbuild -t:BuildManagedPluginPackage -p:Configuration=Release
dotnet msbuild -t:DeployManagedPlugin -p:ManagedPluginDeployRoot=C:\Path\To\Controls
```
