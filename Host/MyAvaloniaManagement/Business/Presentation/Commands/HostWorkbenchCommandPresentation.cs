using System;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Commands.State;
using MyAvaloniaManagement.Business.Diagnostics;

namespace MyAvaloniaManagement.Business.Presentation.Commands;

/// <summary>拥有 Host 打开、保存两个工作台展示适配器。</summary>
/// <remarks>
/// 本对象由根容器作为单例持有，因此 File 菜单与 Window KeyBinding 会取得完全相同的 Save 实例。
/// 它只组合稳定 CommandId、State Query、Executor 与 UI Dispatcher，不持有 Provider、Dock、Document、
/// Control 或插件对象。通用插件菜单和快捷键贡献属于 G5，不进入此 Host-only 模型。
/// </remarks>
internal sealed class HostWorkbenchCommandPresentation :
    IWorkbenchCommandPresentationBindings,
    IDisposable
{
    internal HostWorkbenchCommandPresentation(
        WorkbenchCommandStateQuery states,
        WorkbenchCommandExecutor executor,
        Dispatcher dispatcher,
        IHostDiagnosticSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(dispatcher);

        Open = new WorkbenchPresentationCommand(
            HostWorkbenchCommandIds.OpenDocument,
            states,
            executor,
            dispatcher,
            diagnostics);
        Save = new WorkbenchPresentationCommand(
            HostWorkbenchCommandIds.SaveDocument,
            states,
            executor,
            dispatcher,
            diagnostics);
    }

    /// <summary>获取唯一的 Host 打开命令展示适配器。</summary>
    public IWorkbenchPresentationCommandBinding Open { get; }

    /// <summary>获取由 File 菜单和 Ctrl+S 共享的 Host 保存命令展示适配器。</summary>
    public IWorkbenchPresentationCommandBinding Save { get; }

    /// <summary>成对释放两个适配器对统一状态源的订阅。</summary>
    public void Dispose()
    {
        ((WorkbenchPresentationCommand)Open).Dispose();
        ((WorkbenchPresentationCommand)Save).Dispose();
    }
}
