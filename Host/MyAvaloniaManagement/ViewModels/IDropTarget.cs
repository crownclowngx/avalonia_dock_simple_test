using Avalonia.Input;

namespace MyAvaloniaManagement.ViewModels;

internal interface IDropTarget
{
    void DragOver(object? sender, DragEventArgs e);
    void Drop(object? sender, DragEventArgs e);
}
