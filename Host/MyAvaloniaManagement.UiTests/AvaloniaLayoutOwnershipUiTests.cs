using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// 框架层的队列归属回归，不经过 Dock 或宿主回收器。用于证明修复作用于布局机制，
/// 而非依赖某一种拖动入口恰好先完成目标布局。窗口及视图均由当前用例拥有。
/// </summary>
public sealed class AvaloniaLayoutOwnershipUiTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void 两种队列及窗口处理顺序均能连续跨窗二十次(bool pendingMeasure, bool targetFirst)
    {
        var editor = new TextBox { Text = "保留编辑状态" };
        var view = new Border { Child = new StackPanel { Children = { new TextBlock { Text = "布局测试" }, editor,
            new ScrollViewer { Height = 90, Content = new ItemsControl { ItemsSource = Enumerable.Range(1, 30).ToArray() } } } } };
        var left = new ContentPresenter { Content = view };
        var right = new ContentPresenter();
        var first = new Window { Width = 700, Height = 500, Content = left };
        var second = new Window { Width = 600, Height = 450, Content = right };
        try
        {
            first.Show(); second.Show(); first.UpdateLayout(); second.UpdateLayout();
            for (var index = 0; index < 20; index++)
            {
                var from = index % 2 == 0 ? left : right;
                var to = index % 2 == 0 ? right : left;
                var source = index % 2 == 0 ? first : second;
                var target = index % 2 == 0 ? second : first;
                if (pendingMeasure) { editor.InvalidateMeasure(); view.InvalidateMeasure(); }
                else { editor.InvalidateArrange(); view.InvalidateArrange(); }
                // 摘除后立即转交：这里故意没有处理旧队列，避免测试自身掩盖缺陷。
                from.Content = null; from.UpdateChild();
                to.Content = view; to.UpdateChild();
                if (targetFirst) { target.UpdateLayout(); source.UpdateLayout(); }
                else { source.UpdateLayout(); target.UpdateLayout(); }
                Assert.Null(from.Child);
                Assert.Same(view, to.Child);
                Assert.Same(target, TopLevel.GetTopLevel(view));
                Assert.True(view.IsMeasureValid && view.IsArrangeValid);
                Assert.True(editor.IsMeasureValid && editor.IsArrangeValid);
                Assert.True(editor.Bounds.Width > 0 && editor.Bounds.Height > 0);
                Assert.Equal("保留编辑状态", editor.Text);
                Assert.True(source.IsVisible);
            }
        }
        finally { first.Close(); second.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void 测量或安排祖先回调中迁走子树后旧管理器不继续递归(bool duringMeasure)
    {
        var view = new Border { Child = new TextBlock { Text = "子树" } };
        var from = new CallbackDecorator { Child = view };
        var to = new Decorator();
        var first = new Window { Width = 500, Height = 400, Content = from };
        var second = new Window { Width = 600, Height = 450, Content = to };
        try
        {
            first.Show(); second.Show(); first.UpdateLayout(); second.UpdateLayout();
            var transfers = 0;
            void Transfer() { transfers++; from.Child = null; to.Child = view; }
            if (duringMeasure)
            {
                from.MeasureCallback = Transfer;
                view.InvalidateMeasure(); from.InvalidateMeasure();
            }
            else
            {
                from.ArrangeCallback = Transfer;
                view.InvalidateArrange(); from.InvalidateArrange();
            }
            first.UpdateLayout(); second.UpdateLayout();
            Assert.Equal(1, transfers);
            Assert.Same(second, TopLevel.GetTopLevel(view));
            Assert.True(view.IsMeasureValid && view.IsArrangeValid);
            Assert.True(view.Bounds.Width > 0 && view.Bounds.Height > 0);
        }
        finally { first.Close(); second.Close(); }
    }

    [AvaloniaFact]
    public void 布局失效入口仍拒绝其他窗口的控件()
    {
        var view = new Border();
        var first = new Window { Content = view };
        var second = new Window();
        try
        {
            first.Show(); second.Show();
            var manager = second.GetLayoutManager()!;
            Assert.Throws<ArgumentException>(() => manager.InvalidateMeasure(view));
            Assert.Throws<ArgumentException>(() => manager.InvalidateArrange(view));
        }
        finally { first.Close(); second.Close(); }
    }

    private sealed class CallbackDecorator : Decorator
    {
        internal Action? MeasureCallback { get; set; }
        internal Action? ArrangeCallback { get; set; }
        protected override Size MeasureOverride(Size availableSize)
        {
            var callback = MeasureCallback; MeasureCallback = null;
            callback?.Invoke();
            return base.MeasureOverride(availableSize);
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            var callback = ArrangeCallback; ArrangeCallback = null;
            callback?.Invoke();
            return base.ArrangeOverride(finalSize);
        }
    }
}
