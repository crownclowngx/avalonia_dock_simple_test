using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Controls.Recycling.Model;
using Avalonia.Input;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// 验证文档控件缓存复用、视觉父级解绑及资源释放。
/// </summary>
public sealed class DocumentControlRecyclingTests
{
    [AvaloniaFact]
    public void 缓存命中时复用控件并从原视觉父级解绑()
    {
        var recycling = new DocumentControlRecycling();
        var key = new object();
        var control = new Border();
        var panel = new StackPanel
        {
            Children = { control }
        };
        recycling.Add(key, control);

        var result = recycling.Build(key, null, null);

        Assert.Same(control, result);
        Assert.DoesNotContain(control, panel.Children);
        Assert.True(recycling.TryGetValue(key, out var cached));
        Assert.Same(control, cached);
    }

    [AvaloniaFact]
    public void 删除缓存会断开数据上下文并释放控件()
    {
        var recycling = new DocumentControlRecycling();
        var key = new object();
        var control = new DisposableContentControl
        {
            DataContext = key
        };
        var parent = new StackPanel { Children = { control } };
        KeyboardNavigation.SetTabOnceActiveElement(parent, control);
        recycling.Add(key, control);

        Assert.True(recycling.Remove(key));

        Assert.DoesNotContain(control, parent.Children);
        Assert.Null(KeyboardNavigation.GetTabOnceActiveElement(parent));
        Assert.Null(control.DataContext);
        Assert.True(control.IsDisposed);
        Assert.False(recycling.Remove(key));
    }

    [AvaloniaFact]
    public void 按稳定Id缓存且空输入安全返回()
    {
        var recycling = new DocumentControlRecycling
        {
            TryToUseIdAsKey = true
        };
        var first = new RecyclingKey("stable");
        var second = new RecyclingKey("stable");
        var control = new Border();
        recycling.Add("stable", control);

        Assert.Same(control, recycling.Build(first, null, null));
        Assert.Same(control, recycling.Build(second, null, null));
        Assert.False(recycling.TryGetValue(null, out var missing));
        Assert.Null(missing);
        Assert.Null(recycling.Build(null, null, null));
        Assert.False(recycling.Remove(null));
    }

    [AvaloniaFact]
    public void 移除单个Document不影响其他Document的缓存()
    {
        var recycling = new DocumentControlRecycling();
        var first = new object();
        var second = new object();
        var firstControl = new Border();
        var secondControl = new Border();
        recycling.Add(first, firstControl);
        recycling.Add(second, secondControl);

        Assert.True(recycling.Remove(first));

        Assert.False(recycling.TryGetValue(first, out _));
        Assert.True(recycling.TryGetValue(second, out var remaining));
        Assert.Same(secondControl, remaining);
    }

    private sealed class DisposableContentControl
        : ContentControl, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class RecyclingKey(string id)
        : IControlRecyclingIdProvider
    {
        public string GetControlRecyclingId() => id;
    }
}
