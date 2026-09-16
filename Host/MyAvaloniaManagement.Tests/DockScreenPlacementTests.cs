using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.Tests;

public sealed class DockScreenPlacementTests
{
    [Fact]
    public void 左侧屏幕负坐标合法且保留逻辑尺寸()
    {
        var left = new DockScreenBounds(-1920, 0, 1920, 1040, 1);
        var saved = new DockWindowBounds(-1500, 90, 640, 480, false, left);
        Assert.Equal(saved, DockScreenPlacement.Normalize(saved, [left, new(0, 0, 1920, 1040, 1)]));
    }

    [Fact]
    public void 缩放改变时按逻辑偏移换算屏幕像素()
    {
        var previous = new DockScreenBounds(0, 0, 2560, 1400, 1);
        var current = previous with { Scaling = 2 };
        var result = DockScreenPlacement.Normalize(new(100, 80, 640, 480, false, previous), [current]);
        Assert.Equal(200, result.X);
        Assert.Equal(160, result.Y);
        Assert.Equal(640, result.Width);
        Assert.Equal(480, result.Height);
        Assert.Equal(current, result.Screen);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.75)]
    [InlineData(2)]
    public void 移除原屏幕后整个窗口适应新工作区(double scale)
    {
        var screen = new DockScreenBounds(0, 40, 800, 560, scale);
        var result = DockScreenPlacement.Normalize(new(-4000, -900, 2000, 1400, true,
            new(-4000, 0, 4000, 2000, 1)), [screen]);
        Assert.True(result.X >= screen.X);
        Assert.True(result.Y >= screen.Y);
        Assert.True(result.X + result.Width * scale <= screen.X + screen.Width + 0.001);
        Assert.True(result.Y + result.Height * scale <= screen.Y + screen.Height + 0.001);
        Assert.True(result.Maximized);
        Assert.True(DockScreenPlacement.IsReachable(result, [screen]));
    }

    [Fact]
    public void 小工作区降低最小尺寸且没有屏幕时保留待应用记录()
    {
        var saved = DockWindowBounds.Default;
        Assert.Same(saved, DockScreenPlacement.Normalize(saved, []));
        var result = DockScreenPlacement.Normalize(saved, [new(0, 0, 10, 10, 8)]);
        Assert.True(result.Width > 0);
        Assert.True(result.Height > 0);
        Assert.True(result.Width * 8 <= 10);
        Assert.True(result.Height * 8 <= 10);
    }

    [Fact]
    public void 找回规则检查标题区域而不是窗口底部一角()
    {
        var screen = new DockScreenBounds(0, 0, 1920, 1040, 1);
        Assert.False(DockScreenPlacement.IsReachable(new(100, -500, 640, 600, false, screen), [screen]));
        Assert.True(DockScreenPlacement.IsReachable(new(100, 50, 640, 600, false, screen), [screen]));
        Assert.False(DockScreenPlacement.IsReachable(new(1920, 50, 640, 600, false, screen), [screen]));
    }
}
