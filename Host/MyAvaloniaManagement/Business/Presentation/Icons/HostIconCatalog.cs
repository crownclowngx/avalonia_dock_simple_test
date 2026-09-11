using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MyAvaloniaManagement.Business.Presentation.Icons;

/// <summary>Host 公开给插件元数据使用的固定矢量名称目录，不读取图片、不执行插件代码。</summary>
/// <remarks>
/// 名称与几何内容分离：将来替换图标外观只修改这里，插件仍保存同一名称。
/// 返回的几何路径是只读字符串，不能被用作任意资源键、文件地址或类型名。
/// </remarks>
internal static class HostIconCatalog
{
    internal const string DefaultKey = "builtin:module";
    internal static IReadOnlyDictionary<string, string> Paths { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DefaultKey] = "M2,2 H8 V8 H2 Z M10,2 H16 V8 H10 Z M2,10 H8 V16 H2 Z M10,10 H16 V16 H10 Z",
            ["builtin:folder"] = "M2,4 H8 L10,6 H18 V8 H5 L3,16 H1 Z M5,9 H19 L16,17 H2 Z",
            ["builtin:table"] = "M2,2 H18 V18 H2 Z M4,6 V10 H9 V6 Z M11,6 V10 H16 V6 Z M4,12 V16 H9 V12 Z M11,12 V16 H16 V12 Z",
            ["builtin:chart"] = "M2,2 H4 V16 H18 V18 H2 Z M6,10 H8 V14 H6 Z M10,6 H12 V14 H10 Z M14,3 H16 V14 H14 Z",
            ["builtin:text-check"] = "M2,3 H17 V5 H2 Z M2,7 H12 V9 H2 Z M2,11 H8 V13 H2 Z M9,14 L11,12 L14,15 L18,10 L20,12 L14,19 Z",
            ["builtin:image"] = "M1,2 H19 V18 H1 Z M3,4 V15 L8,9 L11,12 L14,8 L17,12 V4 Z M5,5 H8 V8 H5 Z",
            ["builtin:video"] = "M2,3 H14 V7 L19,4 V16 L14,13 V17 H2 Z M6,6 V14 L12,10 Z",
            ["builtin:download"] = "M8,1 H12 V10 H16 L10,16 L4,10 H8 Z M2,16 H4 V18 H16 V16 H18 V20 H2 Z",
        });

    internal static string ResolveKey(string? key) => key is not null && Paths.ContainsKey(key) ? key : DefaultKey;
}

/// <summary>在 UI 边界把稳定图标名转换成矢量几何；各位置各建控件，仅复用不可修改的图形数据。</summary>
internal sealed class HostIconGeometryConverter : IValueConverter
{
    private readonly Dictionary<string, Geometry> _geometries = new(StringComparer.Ordinal);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = HostIconCatalog.ResolveKey(value as string);
        if (!_geometries.TryGetValue(key, out var geometry))
        {
            geometry = Geometry.Parse(HostIconCatalog.Paths[key]);
            _geometries.Add(key, geometry);
        }
        return geometry;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("图标仅支持从元数据到界面的单向转换。");
}
