using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System.Linq;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagement.Business.ToolCenter;
using MyAvaloniaManagement.ViewModels.ToolCenter;

namespace MyAvaloniaManagement.Views.ToolCenter;

internal sealed partial class ToolCenterWindow : Window
{
    public ToolCenterWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ToolSearchBox.Focus();
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { args.Handled = true; Close(); }
            else if (args.Key == Key.Enter && ToolItemsList.IsKeyboardFocusWithin && !IsButton(args.Source) && DataContext is ToolCenterViewModel vm)
            { args.Handled = true; vm.OpenToolCommand.Execute(vm.SelectedItem); }
        };
        ToolItemsList.DoubleTapped += (_, args) =>
        {
            if (DataContext is ToolCenterViewModel vm && !IsButton(args.Source))
                vm.OpenToolCommand.Execute(vm.SelectedItem);
        };
    }

    private static bool IsButton(object? source) => source is Avalonia.Visual visual &&
        (visual is Button || visual.GetVisualAncestors().Any(parent => parent is Button));

    /// <summary>菜单在打开时捕获当前行，刷新列表后仍对同一 Tool 操作。</summary>
    /// <remarks>这里只创建菜单控件和绑定；分类、收藏与显隐的实际规则仍由 ViewModel 用例处理。</remarks>
    private void OpenToolMenu(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: ToolCenterItem item } button || DataContext is not ToolCenterViewModel vm) return;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "隐藏工具", IsEnabled = item.CanHide, Command = vm.HideToolCommand, CommandParameter = item });
        var categories = new MenuItem { Header = "设置分类", IsEnabled = !item.IsMissing };
        foreach (var category in vm.Categories)
            categories.Items.Add(new MenuItem { Header = category.DisplayName,
                Command = new RelayCommand(() => vm.AssignToolCategory(item, category)) });
        menu.Items.Add(categories);
        if (item.IsFavorite)
        {
            menu.Items.Add(new MenuItem { Header = "常用上移", Command = new RelayCommand(() => vm.MoveToolFavorite(item, -1)) });
            menu.Items.Add(new MenuItem { Header = "常用下移", Command = new RelayCommand(() => vm.MoveToolFavorite(item, 1)) });
        }
        ShowMenu(button, menu);
    }

    private void OpenOrganizeMenu(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button button || DataContext is not ToolCenterViewModel vm) return;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "管理用途分类…", Command = new RelayCommand(() =>
        {
            CategoryManagement.IsVisible = true;
            CategoryManagement.IsExpanded = true;
        }) });
        menu.Items.Add(new MenuItem { Header = "隐藏所有工具（整个工作区）", IsEnabled = vm.CanHideAll, Command = vm.HideAllCommand });
        ShowMenu(button, menu);
    }

    private static void ShowMenu(Button button, ContextMenu menu)
    {
        button.ContextMenu = menu;
        // 菜单关闭后断开按钮引用，避免闭包持续保留旧列表项和窗口 ViewModel。
        menu.Closed += (_, _) => button.ContextMenu = null;
        menu.Open(button);
    }
}
