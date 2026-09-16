using System;
using System.Collections.Generic;
using System.Linq;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 只描述可重建的工具布局；不持有 Dock 对象、Document 身份或业务内容。
/// 捕获后整棵记录树交给写入队列，后台不再访问 UI。所有字段在线格式中均必需，允许为空的值显式写 null。
/// </summary>
internal sealed record DockLayoutSnapshotV3(int SchemaVersion, DockLayoutWindow MainWindow,
    IReadOnlyList<DockLayoutWindow> FloatingWindows, IReadOnlyList<DockLayoutTool> Tools)
{
    internal const int CurrentSchemaVersion = 3;
}

/// <summary>窗口身份与正常状态位置独立于本次原生 Window 对象的寿命。</summary>
internal sealed record DockLayoutWindow(string Id, DockWindowBounds Bounds, DockLayoutNode Root);

/// <summary>位置是屏幕像素，尺寸是 Avalonia 逻辑尺寸；最大化不覆盖正常尺寸。</summary>
internal sealed record DockWindowBounds(double X, double Y, double Width, double Height,
    bool Maximized, DockScreenBounds? Screen)
{
    internal static DockWindowBounds Default { get; } = new(80, 80, 1000, 700, false, null);
}

/// <summary>保存时的屏幕工作区及缩放只是匹配提示，恢复时必须重新校正。</summary>
internal sealed record DockScreenBounds(double X, double Y, double Width, double Height, double Scaling);

/// <summary>
/// 有限布局节点：split、tools、documents。工具归属仅由 ToolIds 表达，避免工具表与树维护相互冲突的位置。
/// documents 只允许作为主窗口唯一占位；它不表示任何真实文档实例。
/// </summary>
internal sealed record DockLayoutNode(string Kind, string Id, double Proportion, string? Orientation,
    IReadOnlyList<DockLayoutNode> Children, IReadOnlyList<string> ToolIds, string? ActiveToolId)
{
    internal static DockLayoutNode Documents() => new("documents", DockLayoutIds.Documents, 1, null, [], [], null);
    internal static DockLayoutNode Group(string id, IEnumerable<string> tools, double proportion = 1, string? active = null) =>
        new("tools", id, proportion, null, [], tools.ToArray(), active);
    internal static DockLayoutNode Split(string id, string orientation, IEnumerable<DockLayoutNode> children, double proportion = 1) =>
        new("split", id, proportion, orientation, children.ToArray(), [], null);
}

/// <summary>工具显隐与备用主窗口停靠位置；不重复记录当前所属组。</summary>
internal sealed record DockLayoutTool(string Id, string State, string ReturnDockId, int ReturnOrder);

/// <summary>纯数据校验先于 UI 修改，拒绝损坏结构；插件是否可用由恢复投影另行决定。</summary>
internal static class DockLayoutV3Validator
{
    internal const int MaximumFileBytes = 1024 * 1024;
    internal const int MaximumDepth = 32;
    internal const int MaximumNodes = 1024;
    internal const int MaximumTools = 512;
    internal const int MaximumWindows = 32;

    internal static void Validate(DockLayoutSnapshotV3 snapshot)
    {
        if (snapshot is null || snapshot.SchemaVersion != 3) Fail("LAYOUT_SCHEMA_UNSUPPORTED");
        if (snapshot!.MainWindow is null || snapshot.FloatingWindows is null || snapshot.Tools is null ||
            snapshot.FloatingWindows.Count > MaximumWindows || snapshot.Tools.Count > MaximumTools) Fail();
        var tools = new Dictionary<string, DockLayoutTool>(StringComparer.Ordinal);
        foreach (var tool in snapshot.Tools!)
        {
            if (tool is null || !IsId(tool.Id) || !tools.TryAdd(tool.Id, tool) ||
                tool.State is not ("visible" or "hidden" or "autoHidden") ||
                !DockLayoutIds.IsToolDockId(tool.ReturnDockId) || tool.ReturnOrder < 0) Fail();
        }
        var windows = new HashSet<string>(StringComparer.Ordinal);
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new HashSet<DockLayoutNode>(ReferenceEqualityComparer.Instance);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var documents = 0;
        foreach (var window in new[] { snapshot.MainWindow }.Concat(snapshot.FloatingWindows!))
        {
            if (window is null || !IsId(window.Id) || !windows.Add(window.Id)) Fail();
            ValidateBounds(window!.Bounds);
            Visit(window.Root, ReferenceEquals(window, snapshot.MainWindow), 0);
        }
        if (documents != 1 || !placed.SetEquals(tools.Keys)) Fail();

        void Visit(DockLayoutNode node, bool main, int depth)
        {
            if (node is null || depth > MaximumDepth || nodes.Count >= MaximumNodes || !nodes.Add(node) ||
                !IsId(node.Id) || !nodeIds.Add(node.Id) || !double.IsFinite(node.Proportion) ||
                node.Proportion <= 0 || node.Proportion > 1 || node.Children is null || node.ToolIds is null) Fail();
            switch (node!.Kind)
            {
                case "documents":
                    if (!main || ++documents != 1 || node.Children!.Count != 0 || node.ToolIds!.Count != 0 ||
                        node.Orientation is not null || node.ActiveToolId is not null) Fail();
                    break;
                case "tools":
                    if (node.Children!.Count != 0 || node.ToolIds!.Count == 0 || node.Orientation is not null) Fail();
                    foreach (var id in node.ToolIds!)
                    {
                        if (!IsId(id) || !placed.Add(id) || !tools.TryGetValue(id, out var tool)) Fail();
                        if (!main && tools[id].State == "autoHidden") Fail();
                    }
                    if (node.ActiveToolId is { } active && (!node.ToolIds.Contains(active) || tools[active].State != "visible")) Fail();
                    break;
                case "split":
                    if (node.Orientation is not ("horizontal" or "vertical") || node.Children!.Count < 2 ||
                        node.ToolIds!.Count != 0 || node.ActiveToolId is not null ||
                        Math.Abs(node.Children.Sum(child => child?.Proportion ?? double.NaN) - 1) > 0.000001) Fail();
                    foreach (var child in node.Children) Visit(child, main, depth + 1);
                    break;
                default: Fail(); break;
            }
        }
    }

    internal static void ValidateBounds(DockWindowBounds bounds)
    {
        if (bounds is null || !Position(bounds.X) || !Position(bounds.Y) || !Size(bounds.Width) || !Size(bounds.Height)) Fail();
        if (bounds!.Screen is { } screen && (!Position(screen.X) || !Position(screen.Y) ||
            !Size(screen.Width) || !Size(screen.Height) || !double.IsFinite(screen.Scaling) || screen.Scaling is < 0.25 or > 8)) Fail();
    }

    internal static bool IsId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128 &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    private static bool Position(double value) => double.IsFinite(value) && Math.Abs(value) <= 10000000;
    private static bool Size(double value) => double.IsFinite(value) && value is > 0 and <= 100000;
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string code = "LAYOUT_V3_STRUCTURE_INVALID") => throw new DockLayoutFormatException(code);
}
