# MyAvaloniaManagement Plugin Templates

安装模板包后，使用 `myavalonia-plugin` 创建包含真实插件程序集、独立 Avalonia 预览程序和测试项目的
解决方案：

```powershell
dotnet new install MyAvaloniaManagement.Plugin.Templates@1.0.0
dotnet new myavalonia-plugin -n ExamplePlugin --plugin-id myavalonia.plugin.example
```

模板固定经过验证的 Plugin SDK、UI SDK 与 Build 包版本，不生成手写 manifest。
