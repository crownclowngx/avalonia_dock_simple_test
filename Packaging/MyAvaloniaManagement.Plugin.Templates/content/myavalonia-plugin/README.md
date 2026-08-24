# DemoPlugin

这是由 `myavalonia-plugin` 创建的 Managed Plugin 解决方案。真实交付物是
`src/DemoPlugin.Plugin`；`Standalone` 只负责快速预览同一份 View、ViewModel 与业务代码。

```powershell
dotnet restore
dotnet build
dotnet run --project src/DemoPlugin.Standalone
dotnet msbuild src/DemoPlugin.Plugin/DemoPlugin.Plugin.csproj -t:BuildManagedPluginPackage -p:Configuration=Release
```

要在真实 Host 中调试，请显式提供 Host 的 `Controls` 目录：

```powershell
dotnet msbuild src/DemoPlugin.Plugin/DemoPlugin.Plugin.csproj `
  -t:DeployManagedPlugin `
  -p:ManagedPluginDeployRoot=C:\Path\To\Host\Controls
```

Standalone 只能验证界面和插件自身对象图；manifest、加载上下文、Document Scope、Dock、Tool 和
生命周期必须使用真实 Host 做最终验收。
