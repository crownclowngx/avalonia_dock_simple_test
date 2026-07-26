using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using MyAvaloniaManagement.Behaviors;

namespace MyAvaloniaManagement.PluginTests;

public sealed class WindowChromeAndDockDragGuardTests
{
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    public void 指针保护只接管未被占用的左键拖拽区域(
        bool isLeftButtonPressed,
        bool hasForeignCapture,
        bool isButtonSource,
        bool expected)
    {
        var actual = DockTabPointerCaptureGuard.ShouldCapture(
            isLeftButtonPressed,
            hasForeignCapture,
            isButtonSource);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void 重复启用和停用指针保护保持幂等()
    {
        var control = new Border();

        DockTabPointerCaptureGuard.SetIsEnabled(control, true);
        DockTabPointerCaptureGuard.SetIsEnabled(control, true);

        Assert.True(DockTabPointerCaptureGuard.GetIsEnabled(control));
        Assert.True(DockTabPointerCaptureGuard.IsAttached(control));

        DockTabPointerCaptureGuard.SetIsEnabled(control, false);
        DockTabPointerCaptureGuard.SetIsEnabled(control, false);

        Assert.False(DockTabPointerCaptureGuard.GetIsEnabled(control));
        Assert.False(DockTabPointerCaptureGuard.IsAttached(control));
    }

    [Fact]
    public void 兜底恢复只清理残留的Dock拖拽视觉状态()
    {
        var control = new Border();
        var originalTransform = new RotateTransform(7);
        control.RenderTransform = new TranslateTransform(24, 0);
        control.SetValue(Panel.ZIndexProperty, 17);
        ((IPseudoClasses)control.Classes).Add(":dragging");

        var recovered = DockTabPointerCaptureGuard.RecoverStaleVisualState(
            control,
            originalTransform);

        Assert.True(recovered);
        Assert.Same(originalTransform, control.RenderTransform);
        Assert.Equal(0, control.GetValue(Panel.ZIndexProperty));
        Assert.DoesNotContain(":dragging", control.Classes);
    }

    [Fact]
    public void 正常完成的拖拽视觉状态不会被兜底覆盖()
    {
        var control = new Border();
        var currentTransform = new TranslateTransform(8, 0);
        var originalTransform = new RotateTransform(5);
        control.RenderTransform = currentTransform;
        control.SetValue(Panel.ZIndexProperty, 9);

        var recovered = DockTabPointerCaptureGuard.RecoverStaleVisualState(
            control,
            originalTransform);

        Assert.False(recovered);
        Assert.Same(currentTransform, control.RenderTransform);
        Assert.Equal(9, control.GetValue(Panel.ZIndexProperty));
    }

    [Fact]
    public void 主窗口固定使用完整系统标题栏且客户区不侵入装饰区()
    {
        var document = XDocument.Load(FindRepositoryFile(
            "Host",
            "MyAvaloniaManagement",
            "Views",
            "MainWindow.axaml"));
        var window = Assert.IsType<XElement>(document.Root);

        Assert.Equal("管理系统", (string?)window.Attribute("Title"));
        Assert.Equal("False", (string?)window.Attribute("ExtendClientAreaToDecorationsHint"));
        Assert.Equal("Full", (string?)window.Attribute("WindowDecorations"));
        Assert.DoesNotContain(
            window.Descendants(),
            element =>
                element.Name.LocalName == "Label" &&
                (string?)element.Attribute("Content") == "管理系统");
    }

    [Fact]
    public void 应用样式覆盖三类Dock标签并保留浮动窗原生边界()
    {
        var document = XDocument.Load(FindRepositoryFile(
            "Host",
            "MyAvaloniaManagement",
            "App.axaml"));
        var styles = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .ToDictionary(
                element => (string)element.Attribute("Selector")!,
                StringComparer.Ordinal);

        AssertStyleSetter(styles, "dock|HostWindow", "WindowDecorations", "Full");
        AssertStyleSetter(
            styles,
            "dock|HostWindow",
            "ExtendClientAreaToDecorationsHint",
            "False");
        AssertStyleSetter(
            styles,
            "dock|HostWindow",
            "ToolChromeControlsWholeWindow",
            "False");
        AssertStyleSetter(
            styles,
            "dock|HostWindow dock|DocumentTabStrip",
            "EnableWindowDrag",
            "False");

        foreach (var selector in new[]
                 {
                     "dock|DocumentTabStripItem",
                     "dock|ToolTabStripItem",
                     "dock|ToolPinItemControl"
                 })
        {
            AssertStyleSetter(
                styles,
                selector,
                "behaviors:DockTabPointerCaptureGuard.IsEnabled",
                "True");
        }
    }

    private static void AssertStyleSetter(
        IReadOnlyDictionary<string, XElement> styles,
        string selector,
        string property,
        string value)
    {
        var style = styles[selector];
        Assert.Contains(
            style.Elements(),
            element =>
                element.Name.LocalName == "Setter" &&
                (string?)element.Attribute("Property") == property &&
                (string?)element.Attribute("Value") == value);
    }

    private static string FindRepositoryFile(params string[] pathSegments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = pathSegments.Aggregate(
                directory.FullName,
                Path.Combine);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"无法从测试输出目录定位仓库文件：{Path.Combine(pathSegments)}");
    }
}
