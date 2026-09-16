# MyAvaloniaManagement Plugin Templates

安装模板包后，使用 `myavalonia-plugin` 创建包含真实插件程序集、独立 Avalonia 预览程序、测试项目和
随项目生成的开发部署文档的解决方案：

```powershell
# 1.4.2 为本地候选；在仓库完成打包后安装，不代表已经公开发布。
dotnet new install ./artifacts/host-v9/feed/MyAvaloniaManagement.Plugin.Templates.1.4.2.nupkg
dotnet new myavalonia-plugin -n ExamplePlugin --plugin-id myavalonia.plugin.example
```

`1.4.2` 固定 Plugin SDK/UI SDK `3.4.1`、Avalonia.Desktop `12.1.2` 与 Build `1.1.3`，不生成手写 manifest。模板包含一个
Document 实例级 Workbench Command 和 Tools 共享菜单声明，但不默认占用快捷键。生成后从项目根
`README.md` 进入 `docs/`，可以查看项目职责、Command 设计、Standalone 边界、临时部署和正式 ZIP 发布说明。


模板同时引用 Icons 1.0.0，并演示公共资源注册为专属入口、同一图形在 MainView/Standalone 直接显示。

新生成插件的最低 SDK 为 3.4.1；既有插件仍可保留原 DLL 和清单，交由新版 Host 的共享程序集策略验证。
候选 SDK 尚未公开发布时，生成项目还原须同时指定本地 SDK feed 与 NuGet.org。验证记录见仓库
`docs/plan-history/host-v9/development-acceptance.md`。此前公开的 1.4.1 / SDK 3.4.0 不被覆盖。
