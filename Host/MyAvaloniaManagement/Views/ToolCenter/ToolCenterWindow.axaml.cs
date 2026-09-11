using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System.Linq;
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
}
