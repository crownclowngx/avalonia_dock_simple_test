using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagement.ViewModels.FunctionCenter;

namespace MyAvaloniaManagement.Views.FunctionCenter;

/// <summary>仅适配窗口输入与关闭行为；搜索和文档创建全部由会话 ViewModel 负责。</summary>
internal sealed partial class FunctionCenterWindow : Window
{
    public FunctionCenterWindow()
    {
        InitializeComponent();
        CancelCreationButton.Click += (_, _) => Close();
        FunctionItemsList.DoubleTapped += OnItemDoubleTapped;
        FunctionCategoryTree.Tapped += OnCategoryTapped;
        Opened += (_, _) => FunctionSearchBox.Focus();
        Closing += (_, args) =>
        {
            // 已开始的初始化不可因关闭选择窗而假称取消；所有权由文档操作链继续持有。
            if (DataContext is FunctionCenterViewModel { IsBusy: true }) args.Cancel = true;
        };
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                if (DataContext is FunctionCenterViewModel { IsBusy: false }) Close();
            }
        }, RoutingStrategies.Tunnel);
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs args)
    {
        // 列表空白处的双击不能再次打开上次选中的条目。
        if (args.Source is Control source && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { } item &&
            DataContext is FunctionCenterViewModel viewModel && !viewModel.IsBusy)
        {
            viewModel.SelectedItem = item.DataContext as Business.Workspace.DocumentCreationItem;
            if (viewModel.CreateCommand.CanExecute(null)) viewModel.CreateCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnCategoryTapped(object? sender, TappedEventArgs args)
    {
        if (args.Source is Control { DataContext: NavigationTreeNode node } &&
            DataContext is FunctionCenterViewModel viewModel)
            viewModel.SelectedCategory = node;
    }
}
