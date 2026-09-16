using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Threading;
using MyAvaloniaManagement.Behaviors;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// 经 Headless 输入设备和真实路由事件验证捕获所有权；不以纯布尔函数替代事件顺序回归。
/// </summary>
public sealed class DockPointerCaptureRegressionTests
{
    [AvaloniaTheory]
    [InlineData("保持挂载")]
    [InlineData("移出视觉树")]
    [InlineData("停用保护")]
    public void 捕获移交后保护层不恢复接收方仍在使用的拖拽状态(string action)
    {
        using var scene = new PointerScene();
        scene.StartDrag();
        scene.Pointer!.Capture(scene.Receiver);
        if (action == "移出视觉树")
            scene.Panel.Children.Remove(scene.Tab);
        else if (action == "停用保护")
            DockTabPointerCaptureGuard.SetIsEnabled(scene.Tab, false);

        Dispatcher.UIThread.RunJobs();

        Assert.Same(scene.Receiver, scene.Pointer.Captured);
        Assert.Contains(":dragging", scene.Tab.Classes);
        Assert.Same(scene.DragTransform, scene.Tab.RenderTransform);
        Assert.Equal(17, scene.Tab.ZIndex);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void 自有手势松开或捕获取消后恢复残留视觉状态(bool loseCapture)
    {
        using var scene = new PointerScene();
        scene.StartDrag();
        if (loseCapture)
            scene.Pointer!.Capture(null);
        else
            scene.Window.MouseUp(new Point(30, 20), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(scene.Pointer!.Captured);
        Assert.DoesNotContain(":dragging", scene.Tab.Classes);
        Assert.Same(scene.OriginalTransform, scene.Tab.RenderTransform);
        Assert.Equal(0, scene.Tab.ZIndex);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void 后代控件接管且没有向共同祖先发送Lost事件时也尊重捕获所有权(bool disable)
    {
        using var scene = new PointerScene();
        scene.StartDrag();
        scene.Panel.Children.Remove(scene.Receiver);
        scene.Tab.Child = scene.Receiver;
        scene.Pointer!.Capture(scene.Receiver);
        if (disable)
            DockTabPointerCaptureGuard.SetIsEnabled(scene.Tab, false);
        else
            scene.Panel.Children.Remove(scene.Tab);

        // 卸载时 Avalonia 可能把捕获转移给视觉祖先；本断言只关心保护层没有恢复仍属接收方的状态。
        Assert.Contains(":dragging", scene.Tab.Classes);
        Assert.Same(scene.DragTransform, scene.Tab.RenderTransform);
    }

    [AvaloniaFact]
    public void 上一次手势的延迟恢复不能清理新手势()
    {
        using var scene = new PointerScene();
        scene.StartDrag();
        // Headless 的 MouseDown 会先抽空队列，因此这里直接发送真实路由事件，
        // 确保旧恢复回调仍在队列时就开始下一次手势。
        scene.Pointer!.Capture(null);
        scene.Tab.RaiseEvent(new PointerPressedEventArgs(scene.Tab, scene.Pointer,
            scene.Tab, new Point(20, 20), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None, 1));
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(":dragging", scene.Tab.Classes);
        Assert.Same(scene.DragTransform, scene.Tab.RenderTransform);
    }

    [AvaloniaFact]
    public void 标签中的按钮点击仍由按钮处理且不被保护层捕获()
    {
        using var scene = new PointerScene();
        var button = new Button { Content = "关闭", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        scene.Tab.Child = button;
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        Dispatcher.UIThread.RunJobs();
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), scene.Window)!.Value;
        scene.Window.MouseDown(point, MouseButton.Left);
        scene.Window.MouseMove(point, RawInputModifiers.LeftMouseButton);
        Assert.NotSame(scene.Tab, scene.Pointer!.Captured);
        scene.Window.MouseUp(point, MouseButton.Left);
        Assert.Equal(1, clicks);
    }

    private sealed class PointerScene : IDisposable
    {
        public readonly RotateTransform OriginalTransform = new(0);
        public readonly TranslateTransform DragTransform = new(4, 0);
        public Border Tab { get; } = new() { Width = 200, Height = 80, Background = Brushes.Gray };
        public Border Receiver { get; } = new() { Width = 200, Height = 80, Background = Brushes.White };
        public StackPanel Panel { get; } = new();
        public Window Window { get; }
        public IPointer? Pointer { get; private set; }

        public PointerScene()
        {
            Tab.RenderTransform = OriginalTransform;
            Tab.AddHandler(InputElement.PointerPressedEvent, (_, e) => Pointer = e.Pointer,
                Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            DockTabPointerCaptureGuard.SetIsEnabled(Tab, true);
            Panel.Children.Add(Tab);
            Panel.Children.Add(Receiver);
            Window = new Window { Width = 200, Height = 160, Content = Panel };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public void StartDrag()
        {
            Window.MouseDown(new Point(20, 20), MouseButton.Left);
            Window.MouseMove(new Point(30, 20), RawInputModifiers.LeftMouseButton);
            Assert.Same(Tab, Pointer!.Captured);
            Tab.RenderTransform = DragTransform;
            Tab.ZIndex = 17;
            ((IPseudoClasses)Tab.Classes).Add(":dragging");
        }

        public void Dispose()
        {
            Window.MouseUp(new Point(30, 20), MouseButton.Left);
            Pointer?.Capture(null);
            DockTabPointerCaptureGuard.SetIsEnabled(Tab, false);
            Window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
