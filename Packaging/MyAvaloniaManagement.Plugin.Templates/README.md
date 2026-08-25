# MyAvaloniaManagement Plugin Templates

安装模板包后，使用 `myavalonia-plugin` 创建包含真实插件程序集、独立 Avalonia 预览程序、测试项目和
随项目生成的开发部署文档的解决方案：

```powershell
dotnet new install MyAvaloniaManagement.Plugin.Templates@1.1.0
dotnet new myavalonia-plugin -n ExamplePlugin --plugin-id myavalonia.plugin.example
```

`1.1.0` 固定 Plugin SDK/UI SDK `3.1.0` 与 Build `1.1.2`，不生成手写 manifest。生成后从项目根
`README.md` 进入 `docs/`，可以查看项目职责、Standalone 边界、临时部署和正式 ZIP 发布说明。
