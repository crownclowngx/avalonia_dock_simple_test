# MyAvaloniaManagement Plugin Templates

> 用途：当前包消费说明；核对日期：2026-09-16。事实源为本包项目、公开类型或随包构建/模板内容。

安装模板包后，使用 `myavalonia-plugin` 创建包含真实插件程序集、独立 Avalonia 预览程序、测试项目和
随项目生成的开发部署文档的解决方案：

```powershell
dotnet new install MyAvaloniaManagement.Plugin.Templates@3.4.1
dotnet new myavalonia-plugin -n ExamplePlugin --plugin-id myavalonia.plugin.example
```

`3.4.1` 固定 Plugin SDK/UI SDK、Icons 与 Build `3.4.1`，Avalonia.Desktop `12.1.2`，不生成手写 manifest。模板包含一个
Document 实例级 Workbench Command 和 Tools 共享菜单声明，但不默认占用快捷键。生成后从项目根
`README.md` 进入 `docs/`，可以查看项目职责、Command 设计、Standalone 边界、临时部署和正式 ZIP 发布说明。


模板同时引用 Icons 3.4.1，并演示公共资源注册为专属入口、同一图形在 MainView/Standalone 直接显示。

新生成插件的最低 SDK 为 3.4.1；既有插件仍可保留原 DLL 和清单，交由新版 Host 的共享程序集策略验证。
统一包版本的发布及公共源消费记录见仓库
`docs/archive/records/host-v9/nuget-unified-3.4.1-release.md`。此前公开版本仍可独立使用。
