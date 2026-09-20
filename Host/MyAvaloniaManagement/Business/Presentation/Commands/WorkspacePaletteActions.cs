using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.ToolCenter;
using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.Business.Presentation.Commands;

/// <summary>一次搜索动作的结果；只携带页面身份，不让共享 Command 持有某个窗口的新建结果。</summary>
internal readonly record struct WorkspacePaletteResult(string Error, WorkspacePageId? PageId = null);

/// <summary>把三个非 Command 的搜索动作适配到现有用例，不拥有业务对象或建立第二套执行门。</summary>
/// <remarks>
/// 功能与页面不是插件 Command。这里保留各自身份和执行结果，真正的串行、创建回滚、
/// 显隐与关闭判断仍由 Coordinator／Workspace／Actions 负责。普通 Command 不经过本类。
/// </remarks>
internal sealed class WorkspacePaletteActions(WorkspaceSession workspace, DocumentCreationMenuQuery functions,
    DocumentPersistenceCoordinator documents, DocumentOperationState operationState,
    ToolWorkspaceReadModel tools, ToolCenterActions toolActions)
{
    internal bool CanExecute(WorkbenchPaletteIdentity identity) => identity switch
    {
        FunctionPaletteIdentity function => workspace.CanCreateDocuments && functions.ReadDirectory().Items.Any(item =>
            item.Entry.DocumentTypeId == function.DocumentTypeId && item.Entry.CreationIntentId == function.IntentId),
        PagePaletteIdentity page => workspace.GetOpenPages().Any(item => item.Id == page.Id && item.CanActivate),
        ToolPaletteIdentity tool => tools.CanOpen(tool.Id.Value),
        _ => false
    };

    /// <summary>返回空文字表示成功；非空文字供搜索会话原地展示，不借用历史全局错误判断本次结果。</summary>
    internal async ValueTask<string> ExecuteAsync(WorkbenchPaletteIdentity identity) =>
        (await ExecuteAsync(identity, null)).Error;

    internal async ValueTask<WorkspacePaletteResult> ExecuteAsync(WorkbenchPaletteIdentity identity, DocumentCreationTarget? target)
    {
        if (!CanExecute(identity)) return new("目标已不可用或正在关闭，请重新选择。");
        switch (identity)
        {
            case FunctionPaletteIdentity function:
                var result = await documents.CreateDocumentAsync(function.DocumentTypeId, function.IntentId, target);
                operationState.Apply(result);
                return new(result.ShouldUpdateError ? result.Error : "功能未能打开，请重试。", result.CreatedPageId);
            case PagePaletteIdentity page:
                return workspace.TryActivatePage(page.Id) ? new(string.Empty, page.Id) : new("原页面已关闭或暂时无法切换。");
            case ToolPaletteIdentity tool:
                return new(toolActions.Open(tool.Id.Value, focus: true).Message);
            default:
                return new("该结果不支持工作区操作。");
        }
    }

    /// <summary>遮罩关闭后再恢复目标焦点；不重新执行创建或记录第二次工具访问。</summary>
    internal void FocusResult(WorkbenchPaletteIdentity identity, WorkspacePageId? resultPage = null, Func<bool>? canFocus = null)
    {
        // 焦点属于桌面展示适配，不让 Workspace 的所有权查询依赖视觉树。
        // 先完成布局再找到可聚焦内容；延迟回调重查目标，防止已经切页后旧搜索抢回焦点。
        var targetId = resultPage ?? workspace.GetActiveDocument()?.PageId;
        void FocusTarget()
        {
            if (canFocus?.Invoke() == false) return;
            Control? view = null;
            if (identity is FunctionPaletteIdentity or PagePaletteIdentity &&
                workspace.GetActiveDocument() is { } page && page.PageId == targetId &&
                (identity is not PagePaletteIdentity selected || page.PageId == selected.Id) &&
                workspace.GetOpenPages().Any(item => item.Id == page.PageId && item.CanActivate))
                view = page.PreparedView;
            else if (identity is ToolPaletteIdentity tool && tools.Capture().Any(item => item.ToolId == tool.Id.Value && item.IsVisible) &&
                workspace.CreatedTools.TryGetValue(tool.Id.Value, out var adapter))
                view = (adapter as ManagedToolDockable)?.PreparedView;
            if (view is null || TopLevel.GetTopLevel(view) is not Window { IsVisible: true }) return;
            TopLevel.GetTopLevel(view)?.UpdateLayout();
            if (!view.Focus()) view.GetVisualDescendants().OfType<InputElement>()
                .FirstOrDefault(element => element.Focusable && element.IsEffectivelyVisible && element.IsEffectivelyEnabled)?.Focus();
        }
        FocusTarget();
        Dispatcher.UIThread.Post(FocusTarget, DispatcherPriority.Background);
    }
}

/// <summary>每个展示项只保存强类型身份与 Host 用例引用，执行期间拒绝同一绑定重复提交。</summary>
internal sealed class WorkspacePaletteCommand(WorkbenchPaletteIdentity identity, WorkspacePaletteActions actions)
    : ObservableObject, IWorkbenchPresentationCommandBinding
{
    private bool _executing;
    public bool IsEnabled => !_executing && actions.CanExecute(identity);
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => IsEnabled;
    public void Execute(object? parameter) => _ = ExecuteAsync();

    /// <summary>观察包括意外 Host 异常在内的全部异步结果，窗口可等待完成后决定是否关闭。</summary>
    internal async ValueTask<string> ExecuteAsync() => (await ExecuteWithTargetAsync(null)).Error;

    /// <summary>目标和结果只属于这次调用；主窗与浮窗共用展示命令时不会互相覆盖请求状态。</summary>
    internal async ValueTask<WorkspacePaletteResult> ExecuteWithTargetAsync(DocumentCreationTarget? target)
    {
        if (_executing) return new("正在处理当前操作，请等待完成。");
        _executing = true;
        try { return await actions.ExecuteAsync(identity, target); }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Palette errorCode=WORKSPACE_ACTION_FAILED type={exception.GetType().Name}");
            return new("操作未完成，请重试或查看插件状态。");
        }
        finally
        {
            _executing = false;
            // 异步 ICommand 入口也必须观察完成；展示观察者的异常不能变成未观察任务。
            try { OnPropertyChanged(nameof(IsEnabled)); }
            catch (Exception exception) { ReportObserver(exception); }
            foreach (EventHandler handler in CanExecuteChanged?.GetInvocationList() ?? [])
            {
                try { handler(this, EventArgs.Empty); }
                catch (Exception exception) { ReportObserver(exception); }
            }
        }
    }

    private static void ReportObserver(Exception exception) =>
        Console.Error.WriteLine($"Palette errorCode=PALETTE_OBSERVER_FAILED type={exception.GetType().Name}");

    internal void FocusResult(WorkspacePageId? page = null, Func<bool>? canFocus = null) => actions.FocusResult(identity, page, canFocus);
}
