using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Data;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels.Bindings;

namespace MyAvaloniaManagement.Views;

/// <summary>把声明式菜单投影转换为当前 View 独占的 Avalonia 控件。</summary>
/// <remarks>
/// Projection 不持有 Control；本 View 只移除自己创建的 MenuItem/Separator，因而不会破坏 XAML 中
/// Host 保留的四个顶级容器和“主题”子菜单。视觉树分离和 DataContext 切换都会成对解除订阅。
/// </remarks>
internal sealed partial class MenuView : UserControl
{
    private readonly List<(MenuItem Container, Control Item)> _generatedItems = [];
    private IWorkbenchMenuProjection? _projection;
    private bool _attached;

    public MenuView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            AttachProjection();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            DetachProjection();
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_attached)
        {
            AttachProjection();
        }
    }

    private void AttachProjection()
    {
        var next = (DataContext as IMainWindowViewBindings)?.WorkbenchCommands.Menu;
        if (ReferenceEquals(next, _projection))
        {
            RebuildGeneratedItems();
            return;
        }

        DetachProjection();
        _projection = next;
        if (_projection is not null)
        {
            _projection.Changed += OnProjectionChanged;
            RebuildGeneratedItems();
        }
    }

    private void DetachProjection()
    {
        if (_projection is not null)
        {
            _projection.Changed -= OnProjectionChanged;
            _projection = null;
        }
        RemoveGeneratedItems();
    }

    private void OnProjectionChanged(object? sender, EventArgs args)
    {
        if (Dispatcher.CheckAccess())
        {
            RebuildGeneratedItems();
        }
        else
        {
            // 生产 Projection 已负责回到 UI Dispatcher；这里保留纵深保护，避免测试替身或
            // 未来实现从工作线程直接修改 Avalonia Items 集合。
            Dispatcher.Post(RebuildGeneratedItems);
        }
    }

    private void RebuildGeneratedItems()
    {
        RemoveGeneratedItems();
        if (!_attached || _projection is null)
        {
            return;
        }

        AddLocation(FileMenu, WorkbenchMenuLocations.FileShared);
        AddLocation(ViewMenu, WorkbenchMenuLocations.ViewShared);
        AddLocation(ToolsMenu, WorkbenchMenuLocations.ToolsShared);
        AddLocation(HelpMenu, WorkbenchMenuLocations.HelpShared);
    }

    private void AddLocation(MenuItem container, MenuLocationId locationId)
    {
        foreach (var entry in _projection!.GetItems(locationId))
        {
            Control control = entry switch
            {
                WorkbenchMenuSeparatorProjectionEntry => new Separator(),
                WorkbenchMenuCommandProjectionEntry command => CreateMenuItem(command),
                _ => throw new InvalidOperationException("未知的工作台菜单投影条目。"),
            };
            container.Items.Add(control);
            _generatedItems.Add((container, control));
        }
    }

    private static MenuItem CreateMenuItem(WorkbenchMenuCommandProjectionEntry entry)
    {
        var item = new MenuItem
        {
            Header = entry.Header,
            Command = entry.Command,
        };
        // Avalonia 并不保证 MenuItem 初次挂载就把 ICommand.CanExecute 写入 IsEnabled。
        // 显式绑定 Adapter 的实时只读属性，同时仍让真正执行进入 Executor 的最终重查。
        item.Bind(
            MenuItem.IsEnabledProperty,
            new Binding(nameof(IWorkbenchPresentationCommandBinding.IsEnabled))
            {
                Source = entry.Command,
                Mode = BindingMode.OneWay,
            });
        return item;
    }

    private void RemoveGeneratedItems()
    {
        foreach (var (container, control) in _generatedItems)
        {
            _ = container.Items.Remove(control);
            if (control is MenuItem menuItem)
            {
                menuItem.Command = null;
                menuItem.ClearValue(MenuItem.IsEnabledProperty);
            }
        }
        _generatedItems.Clear();
    }
}
