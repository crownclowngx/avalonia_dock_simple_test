# MyAvaloniaManagement.Icons

公共单色矢量资源包，独立版本 `1.0.0`，目标 .NET 10；没有 Avalonia、SDK、Host 或 DI 依赖。

```csharp
var asset = CommonIcons.Table;
// 在自己的 UI 中使用 asset.PathData、ViewBoxWidth、ViewBoxHeight，按 EvenOdd 填充。
```

资源均使用 20×20 逻辑画布，名称与属性如下：

| 属性 | 名称 |
| --- | --- |
| Module | builtin:module |
| Folder | builtin:folder |
| Table | builtin:table |
| Chart | builtin:chart |
| TextCheck | builtin:text-check |
| Image | builtin:image |
| Video | builtin:video |
| Download | builtin:download |

`CommonIcons.All` 为完整只读目录。资源延续本仓 V6 手写基础几何，采用仓库 MIT 许可证，不含第三方图片、字体或远程资源。

搭配 UI SDK 3.4.0，可将公共图形声明为插件自己的图标：

```csharp
var asset = CommonIcons.Table;
var reference = registration.AddIcon("review",
    new VectorIconDefinition(asset.PathData, asset.ViewBoxWidth, asset.ViewBoxHeight));
// 将 reference 赋给 DocumentDescriptor 的 iconPath。
```

直接使用 `asset.Key` 跟随 Host 的公共资源版本；注册为专属引用则使用本插件交付的图形。
本包没有全局注册表。资源对象不跨 Host/插件契约传递，复制基础字段即可。
更新包后需要重新构建并交付引用它的程序，不会自动更新已安装程序中的 DLL。
