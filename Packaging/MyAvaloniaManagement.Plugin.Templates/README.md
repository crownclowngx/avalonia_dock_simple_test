# MyAvaloniaManagement Plugin Templates

安装模板包后，使用 `myavalonia-plugin` 创建包含真实插件程序集、独立 Avalonia 预览程序、测试项目和
随项目生成的开发部署文档的解决方案：

```powershell
dotnet new install MyAvaloniaManagement.Plugin.Templates@1.4.1
dotnet new myavalonia-plugin -n ExamplePlugin --plugin-id myavalonia.plugin.example
```

`1.4.1` 固定 Plugin SDK/UI SDK `3.4.0` 与 Build `1.1.3`，不生成手写 manifest。模板包含一个
Document 实例级 Workbench Command 和 Tools 共享菜单声明，但不默认占用快捷键。生成后从项目根
`README.md` 进入 `docs/`，可以查看项目职责、Command 设计、Standalone 边界、临时部署和正式 ZIP 发布说明。


模板同时引用 Icons 1.0.0，并演示公共资源注册为专属入口、同一图形在 MainView/Standalone 直接显示。

1.4.1 的生成代码、依赖和 lock 文件延续 1.4.0。已发布包的 README 使用仍兼容的 `::` 安装语法；
当前仓库说明使用 .NET 9.0.200 及以后推荐的 `@`，并已用公开源安装验证。此次说明更正没有覆盖已发布制品。
