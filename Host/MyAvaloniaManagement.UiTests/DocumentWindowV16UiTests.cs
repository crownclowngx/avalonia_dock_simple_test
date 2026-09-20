using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MyAvaloniaManagement.Business.Layout;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>从真实标签按钮及快捷键进入生产链，先固定 V16 两个故障的行为回归。</summary>
public sealed partial class DocumentWindowV16UiTests
{
    [AvaloniaFact]
    public async Task C01最后文档标签按钮关闭后回收浮窗和文档资源()
    {
        await using var test = new DocumentWindowTestContext();
        var page = await test.Create();
        var model = Assert.IsType<DocumentWindowTestContext.Model>(page.Model);
        var view = Assert.IsType<DocumentWindowTestContext.EditorView>(page.PreparedView);
        var window = await test.Float(page);
        var dockWindow = DockTreeNavigator.FindWindow(test.Workspace.RootDock!, page)!;
        var closed = 0;
        window.Closed += (_, _) => closed++;
        await DocumentWindowTestContext.ClickClose(window, page);
        await DocumentWindowTestContext.Flush();
        Assert.False(window.IsVisible);
        Assert.Equal(1, closed);
        Assert.Empty(test.Workspace.DockFactory.HostWindows);
        Assert.Empty(DockTreeNavigator.EnumerateWindows(test.Workspace.RootDock!));
        Assert.Null(dockWindow.Layout);
        Assert.Null(dockWindow.Factory);
        Assert.Equal(1, model.DisposeCount);
        Assert.Equal(1, view.DisposeCount);
        Assert.True(test.Main.IsVisible);
    }

    [AvaloniaFact]
    public async Task N01浮窗快捷键新建进入发起文档组且逐页关闭回收窗口()
    {
        await using var test = new DocumentWindowTestContext();
        var first = await test.Create();
        var window = await test.Float(first);
        var group = first.Owner;
        var palette = await DocumentWindowTestContext.OpenPalette(window);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await palette.CurrentExecution;
        await DocumentWindowTestContext.Flush();
        var created = test.Workspace.GetDocuments().Single(page => !ReferenceEquals(page, first) && page.Model is DocumentWindowTestContext.Model);
        Assert.Same(group, created.Owner);
        Assert.Same(window, DockTreeNavigator.FindWindow(test.Workspace.RootDock!, created)!.Host);
        test.Workspace.DockFactory.CloseDockable(first);
        await DocumentWindowTestContext.Flush();
        Assert.True(window.IsVisible);
        test.Workspace.DockFactory.CloseDockable(created);
        await DocumentWindowTestContext.Flush();
        Assert.False(window.IsVisible);
        Assert.All(test.State.Models, model => Assert.Equal(1, model.DisposeCount));
    }
}
