using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Controls.Recycling.Model;
using Avalonia.Input;
using Avalonia.VisualTree;
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

    [AvaloniaFact]
    public void 尚未生成模板的逻辑Content父级也必须先解绑且保留绑定()
    {
        var view = new Border();
        var source = new ContentSource { Value = view };
        var owner = new ContentControl();
        using var binding = owner.Bind(ContentControl.ContentProperty, new Binding(nameof(ContentSource.Value)) { Source = source });
        Assert.Same(owner, view.Parent);
        Assert.Null(view.GetVisualParent());
        var recycling = new DocumentControlRecycling();
        recycling.Add(source, view);

        Assert.Same(view, recycling.Build(source, null, null));

        Assert.Null(owner.Content);
        Assert.Null(view.Parent);
        source.Value = new Border();
        Assert.Same(source.Value, owner.Content);
    }

    [AvaloniaFact]
    public void 模板生成的正文优先从实际Presenter立即解绑且不删除内容绑定()
    {
        var view = new Border();
        var source = new ContentSource { Value = "first" };
        var presenter = new ContentPresenter
        {
            ContentTemplate = new FuncDataTemplate<string>((data, _) => data is null ? null : view)
        };
        using var binding = presenter.Bind(ContentPresenter.ContentProperty,
            new Binding(nameof(ContentSource.Value)) { Source = source });
        presenter.UpdateChild();
        Assert.Same(view, presenter.Child);
        var recycling = new DocumentControlRecycling();
        recycling.Add(source, view);

        Assert.Same(view, recycling.Build(source, null, null));

        Assert.Null(presenter.Child);
        Assert.Null(view.GetVisualParent());
        source.Value = "second";
        Assert.Equal("second", presenter.Content);
    }

    [AvaloniaFact]
    public void 装饰器解绑不覆盖其Child绑定()
    {
        var view = new Border();
        var source = new ContentSource { Value = view };
        var owner = new Border();
        using var binding = owner.Bind(Decorator.ChildProperty, new Binding(nameof(ContentSource.Value)) { Source = source });
        var recycling = new DocumentControlRecycling();
        recycling.Add(source, view);

        Assert.Same(view, recycling.Build(source, null, null));

        Assert.Null(owner.Child);
        source.Value = new Border();
        Assert.Same(source.Value, owner.Child);
    }

    [AvaloniaFact]
    public void 模板在空内容下仍返回旧View时拒绝把它交给第二个宿主()
    {
        var view = new Border();
        var presenter = new ContentPresenter
        {
            Content = "first",
            ContentTemplate = new FuncDataTemplate<string>((_, _) => view)
        };
        presenter.UpdateChild();
        var recycling = new DocumentControlRecycling();
        recycling.Add("key", view);

        Assert.Throws<InvalidOperationException>(() => recycling.Build("key", null, null));

        Assert.Same(presenter, view.GetVisualParent());
    }

    [AvaloniaFact]
    public void 无法安全解绑时明确失败且保留原View和缓存以便诊断()
    {
        var view = new Border();
        var owner = new VisualOnlyHost(view);
        var key = new object();
        var recycling = new DocumentControlRecycling();
        recycling.Add(key, view);

        var error = Assert.Throws<InvalidOperationException>(() => recycling.Build(key, null, null));

        Assert.Contains(nameof(VisualOnlyHost), error.Message);
        Assert.Same(owner, view.GetVisualParent());
        Assert.True(recycling.TryGetValue(key, out var cached));
        Assert.Same(view, cached);
    }

    [AvaloniaFact]
    public void 最终关闭即使解绑失败也移除缓存并释放资源一次()
    {
        var key = new object();
        var view = new DisposableContentControl { DataContext = key };
        _ = new VisualOnlyHost(view);
        var recycling = new DocumentControlRecycling();
        recycling.Add(key, view);

        Assert.Throws<InvalidOperationException>(() => recycling.Remove(key));

        Assert.False(recycling.TryGetValue(key, out _));
        Assert.Null(view.DataContext);
        Assert.Equal(1, view.DisposeCount);
        Assert.False(recycling.Remove(key));
        Assert.Equal(1, view.DisposeCount);
    }

    private sealed class VisualOnlyHost : Control
    {
        public VisualOnlyHost(Control child) => VisualChildren.Add(child);
    }

    private sealed class ContentSource : INotifyPropertyChanged
    {
        private object? _value;
        public object? Value
        {
            get => _value;
            set { _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class DisposableContentControl
        : ContentControl, IDisposable
    {
        public int DisposeCount { get; private set; }
        public bool IsDisposed => DisposeCount > 0;

        public void Dispose() => DisposeCount++;
    }

    private sealed class RecyclingKey(string id)
        : IControlRecyclingIdProvider
    {
        public string GetControlRecyclingId() => id;
    }
}
