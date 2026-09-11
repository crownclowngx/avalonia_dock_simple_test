using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Presentation.Icons;

/// <summary>UI 线程内的几何适配与缓存；生命周期属于 Runtime，既不共享控件也不缓存画刷。</summary>
internal sealed class HostIconRenderer(HostIconCatalog catalog)
{
    private readonly Dictionary<VectorIconDefinition, Geometry?> _geometries = [];

    internal event EventHandler<PluginAvailabilityChangedEventArgs>? Changed
    {
        add => catalog.Changed += value;
        remove => catalog.Changed -= value;
    }

    internal (Geometry Geometry, VectorIconDefinition Definition) Resolve(HostIconRequest? request)
    {
        Dispatcher.UIThread.VerifyAccess();
        var icon = catalog.Resolve(request);
        if (!_geometries.TryGetValue(icon.Definition, out var geometry))
        {
            try
            {
                var path = PathGeometry.Parse(icon.Definition.PathData);
                // 契约中的 FillRule 为最终事实，即使路径自身带 F0/F1 前缀也不能覆盖它。
                path.FillRule = icon.Definition.FillRule == IconFillRule.EvenOdd
                    ? FillRule.EvenOdd : FillRule.NonZero;
                var bounds = path.Bounds;
                if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) ||
                    !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
                    bounds.Width <= 0 || bounds.Height <= 0)
                    throw new FormatException("图标没有可绘制的有限面积。");
                geometry = path;
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidOperationException or OverflowException or InvalidDataException)
            {
                // 失败也缓存，列表重绘时不反复解析同一坏路径；默认图形由公共资源测试保证有效。
                geometry = null;
            }
            _geometries.Add(icon.Definition, geometry);
        }

        if (geometry is not null) return (geometry, icon.Definition);
        catalog.ReportOnce(request ?? new HostIconRequest(null, icon.Reference), "ICON_GEOMETRY_INVALID");
        if (icon.Reference == HostIconCatalog.DefaultKey)
            throw new InvalidOperationException("公共默认图标资源无效。");
        return Resolve(null);
    }
}
