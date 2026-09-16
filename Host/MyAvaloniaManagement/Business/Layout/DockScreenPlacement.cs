using System;
using System.Collections.Generic;
using System.Linq;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>只按像素工作区与逻辑尺寸计算窗口恢复位置；不访问 Screens 或原生窗口。</summary>
internal static class DockScreenPlacement
{
    internal static DockWindowBounds Normalize(DockWindowBounds saved, IReadOnlyList<DockScreenBounds> screens, DockScreenBounds? preferred = null)
    {
        DockLayoutV3Validator.ValidateBounds(saved);
        if (screens.Count == 0) return saved;
        var original = saved.Screen;
        var matched = original is null ? null : screens.FirstOrDefault(screen =>
            screen.X == original.X && screen.Y == original.Y && screen.Width == original.Width && screen.Height == original.Height);
        var scale = original?.Scaling ?? 1;
        var screen = matched ?? screens.OrderByDescending(candidate => Overlap(saved.X, saved.Y, saved.Width * scale, saved.Height * scale, candidate))
            .ThenBy(candidate => Distance(saved.X, saved.Y, candidate))
            .ThenBy(candidate => Equals(candidate, preferred) ? 0 : 1).First();
        var maxWidth = Math.Max(1, screen.Width / screen.Scaling - 24);
        var maxHeight = Math.Max(1, screen.Height / screen.Scaling - 24);
        var width = Math.Clamp(saved.Width, Math.Min(320, maxWidth), maxWidth);
        var height = Math.Clamp(saved.Height, Math.Min(240, maxHeight), maxHeight);
        // 同一物理工作区只改变缩放时保留逻辑偏移。没有可靠屏幕匹配则以实际交叠/距离选择，
        // 不把显示器曾经叫过某个名字当作稳定身份，也不把负坐标视为损坏。
        var x = matched is not null && original is not null
            ? screen.X + (saved.X - original.X) / original.Scaling * screen.Scaling : saved.X;
        var y = matched is not null && original is not null
            ? screen.Y + (saved.Y - original.Y) / original.Scaling * screen.Scaling : saved.Y;
        var gapX = Math.Min(12 * screen.Scaling, Math.Max(0, (screen.Width - width * screen.Scaling) / 2));
        var gapY = Math.Min(12 * screen.Scaling, Math.Max(0, (screen.Height - height * screen.Scaling) / 2));
        x = Math.Clamp(x, screen.X + gapX, Math.Max(screen.X + gapX, screen.X + screen.Width - width * screen.Scaling - gapX));
        y = Math.Clamp(y, screen.Y + gapY, Math.Max(screen.Y + gapY, screen.Y + screen.Height - height * screen.Scaling - gapY));
        return saved with { X = x, Y = y, Width = width, Height = height, Screen = screen };
    }

    /// <summary>至少有可抓取的标题区域才算可操作，只有底部一角留在屏幕内不算找回成功。</summary>
    internal static bool IsReachable(DockWindowBounds bounds, IReadOnlyList<DockScreenBounds> screens) =>
        screens.Any(screen => Overlap(bounds.X, bounds.Y,
            Math.Min(160, bounds.Width) * (bounds.Screen?.Scaling ?? screen.Scaling),
            Math.Min(32, bounds.Height) * (bounds.Screen?.Scaling ?? screen.Scaling), screen) >=
            Math.Min(80, bounds.Width) * Math.Min(24, bounds.Height) * screen.Scaling * screen.Scaling);

    private static double Overlap(double x, double y, double width, double height, DockScreenBounds screen) =>
        Math.Max(0, Math.Min(x + width, screen.X + screen.Width) - Math.Max(x, screen.X)) *
        Math.Max(0, Math.Min(y + height, screen.Y + screen.Height) - Math.Max(y, screen.Y));

    private static double Distance(double x, double y, DockScreenBounds screen)
    {
        var dx = x - Math.Clamp(x, screen.X, screen.X + screen.Width);
        var dy = y - Math.Clamp(y, screen.Y, screen.Y + screen.Height);
        return dx * dx + dy * dy;
    }
}
