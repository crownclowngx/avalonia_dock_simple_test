using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Business.Presentation.Icons;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>用真实 Skia 像素与生产渲染器验证矢量语义，不以属性值相等代替视觉行为。</summary>
public sealed class IconRenderingUiTests
{
    private static readonly PluginId Owner = new("myavalonia.plugin.render-icons");

    [AvaloniaFact]
    public void 非正方形画布保持原点留白等比缩放且两个视图颜色独立()
    {
        var (renderer, _, key) = Create(new("M10,5 H30 V15 H10 Z", 40, 20));
        var first = new HostIconView { Renderer = renderer, Source = new(Owner, key), Foreground = Brushes.Red };
        var second = new HostIconView { Renderer = renderer, Source = new(Owner, key), Foreground = Brushes.Blue };
        Assert.Same(renderer.Resolve(first.Source).Geometry, renderer.Resolve(second.Source).Geometry);
        using var red = Render(first);
        using var blue = Render(second);
        Assert.Equal(Colors.Red, Pixel(red, 50, 50));
        Assert.Equal(Colors.Blue, Pixel(blue, 50, 50));
        Assert.Equal(0, Pixel(red, 10, 50).A);
        Assert.Equal(0, Pixel(red, 50, 20).A);
        Assert.Equal(0, Pixel(red, 50, 70).A);
        Assert.Equal(Colors.Red, Pixel(red, 30, 45));
    }

    [AvaloniaTheory]
    [InlineData(IconFillRule.EvenOdd, false)]
    [InlineData(IconFillRule.NonZero, true)]
    public void 填充规则控制嵌套区域且优先于路径前缀(IconFillRule rule, bool filled)
    {
        var prefix = rule == IconFillRule.EvenOdd ? "F1 " : "F0 ";
        var (renderer, _, key) = Create(new(prefix + "M0,0 H20 V20 H0 Z M5,5 H15 V15 H5 Z", 20, 20, rule));
        using var image = Render(new() { Renderer = renderer, Source = new(Owner, key), Foreground = Brushes.Red });
        Assert.Equal(filled ? 255 : 0, Pixel(image, 50, 50).A);
        Assert.Equal(Colors.Red, Pixel(image, 10, 10));
    }

    [AvaloniaFact]
    public void 超出声明画布的路径不会溢出到居中留白()
    {
        var (renderer, _, key) = Create(new("M-10,-20 H50 V40 H-10 Z", 40, 20));
        using var image = Render(new() { Renderer = renderer, Source = new(Owner, key), Foreground = Brushes.Red });
        Assert.Equal(Colors.Red, Pixel(image, 50, 50));
        Assert.Equal(0, Pixel(image, 50, 10).A);
        Assert.Equal(0, Pixel(image, 50, 90).A);
    }

    [AvaloniaTheory]
    [InlineData("not a geometry")]
    [InlineData("M0,0")]
    public void 无效路径只诊断一次且保留贡献和默认图标(string path)
    {
        var sink = new Sink();
        var (renderer, state, key) = Create(new(path, 20, 20), sink);
        var first = renderer.Resolve(new(Owner, key));
        var second = renderer.Resolve(new(Owner, key));
        Assert.Same(first.Geometry, second.Geometry);
        Assert.Same(renderer.Resolve(null).Geometry, first.Geometry);
        Assert.Equal("ICON_GEOMETRY_INVALID", Assert.Single(sink.Drafts).Code);
        Assert.True(new PluginAvailabilityReadModel(state).IsAvailable(Owner));
    }

    [AvaloniaFact]
    public void 缓存之后禁用所有者仍降级并且另一个Runtime不复用几何()
    {
        var definition = new VectorIconDefinition("M0,0 H10 V20 H0 Z", 20, 20);
        var (first, state, key) = Create(definition);
        var (second, _, _) = Create(definition);
        var original = first.Resolve(new(Owner, key)).Geometry;
        Assert.NotSame(original, second.Resolve(new(Owner, key)).Geometry);
        state.BeginShutdown();
        Assert.Same(first.Resolve(null).Geometry, first.Resolve(new(Owner, key)).Geometry);
        Assert.NotSame(original, first.Resolve(new(Owner, key)).Geometry);
    }

    private static (HostIconRenderer Renderer, PluginLifecycleStateStore State, string Key) Create(
        VectorIconDefinition definition, IHostDiagnosticSink? sink = null)
    {
        var builder = new PluginRegistryBuilder();
        var key = builder.AddIcon(Owner, "sample", definition);
        var registry = builder.Build(null);
        var state = new PluginLifecycleStateStore(registry);
        return (new(new(registry, new(state), sink)), state, key);
    }

    private static RenderTargetBitmap Render(HostIconView view)
    {
        view.Measure(new Size(100, 100));
        view.Arrange(new Rect(0, 0, 100, 100));
        var bitmap = new RenderTargetBitmap(new PixelSize(100, 100), new Vector(96, 96));
        bitmap.Render(view);
        return bitmap;
    }

    private static Color Pixel(Bitmap bitmap, int x, int y)
    {
        var pointer = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), pointer, 4, 4);
            var bytes = new byte[4];
            Marshal.Copy(pointer, bytes, 0, 4);
            Assert.True(bitmap.Format == PixelFormat.Bgra8888 || bitmap.Format == PixelFormat.Rgba8888);
            return bitmap.Format == PixelFormat.Bgra8888
                ? Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0])
                : Color.FromArgb(bytes[3], bytes[0], bytes[1], bytes[2]);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private sealed class Sink : IHostDiagnosticSink
    {
        internal List<HostDiagnosticDraft> Drafts { get; } = [];
        public HostDiagnosticRecord Report(HostDiagnosticDraft draft)
        {
            Drafts.Add(draft);
            return new HostDiagnosticRecord
            {
                SessionId = Guid.Empty, Sequence = Drafts.Count, TimestampUtc = DateTimeOffset.UnixEpoch,
                Code = draft.Code, Phase = draft.Phase, Severity = HostDiagnosticSeverity.Warning,
                Disposition = HostDiagnosticDisposition.Continue, UserMessage = "测试诊断",
            };
        }
    }
}
