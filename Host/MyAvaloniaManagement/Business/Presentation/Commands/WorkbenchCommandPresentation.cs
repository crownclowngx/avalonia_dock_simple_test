using System;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Commands.State;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Business.ToolCenter;
using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.Business.Presentation.Commands;

/// <summary>组合并拥有菜单、快捷键、Palette 和共享命令 Adapter 的根级展示模型。</summary>
/// <remarks>
/// 先建立共享 Store，再依次组合读取它的投影；各投影保留各自的刷新与异常政策。
/// 释放时先停止 Palette、菜单和快捷键观察者，最后释放 Store，避免观察者访问已释放的适配器。
/// 该所有权只覆盖 Host 展示对象，不延长 Document、插件 Provider 或 View 的业务寿命。
/// </remarks>
internal sealed class WorkbenchCommandPresentation :
    IWorkbenchCommandPresentationBindings,
    IDisposable
{
    private readonly WorkbenchPresentationCommandStore _commands;
    private readonly WorkbenchMenuProjection _menu;
    private readonly WorkbenchKeyBindingProjection _keyBindings;
    private readonly WorkbenchCommandPaletteProjection _palette;
    private bool _disposed;

    internal WorkbenchCommandPresentation(
        HostWorkbenchCommandProjectionCatalog host,
        PluginRegistry plugins,
        WorkbenchCommandCatalog catalog,
        WorkbenchCommandStateQuery states,
        WorkbenchCommandExecutor executor,
        PluginAvailabilityReadModel availability,
        Dispatcher dispatcher,
        IHostDiagnosticSink? diagnostics = null,
        ToolWorkspaceReadModel? tools = null,
        WorkspaceSession? workspace = null,
        DocumentCreationMenuQuery? functions = null,
        WorkspacePaletteActions? workspaceActions = null,
        Business.Presentation.Icons.HostIconRenderer? icons = null)
    {
        _commands = new WorkbenchPresentationCommandStore(
            catalog,
            states,
            executor,
            dispatcher,
            diagnostics);
        _menu = new WorkbenchMenuProjection(
            host,
            plugins,
            catalog,
            states,
            availability,
            _commands,
            dispatcher,
            diagnostics);
        _keyBindings = new WorkbenchKeyBindingProjection(
            host,
            plugins,
            availability,
            _commands,
            dispatcher,
            diagnostics);
        _palette = new WorkbenchCommandPaletteProjection(
            host,
            plugins,
            catalog,
            states,
            _keyBindings,
            _commands,
            dispatcher,
            diagnostics,
            tools,
            workspace, functions, workspaceActions, icons);
    }

    public IWorkbenchMenuProjection Menu => _menu;

    public IWorkbenchKeyBindingProjection KeyBindings => _keyBindings;

    public IWorkbenchCommandPaletteProjection Palette => _palette;

    /// <summary>按“观察者 → Adapter”顺序解除订阅，防止释放过程中产生新的 View 刷新。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _palette.Dispose();
        _menu.Dispose();
        _keyBindings.Dispose();
        _commands.Dispose();
    }
}
