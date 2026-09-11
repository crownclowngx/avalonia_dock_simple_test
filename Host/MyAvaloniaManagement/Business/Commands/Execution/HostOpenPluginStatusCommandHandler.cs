using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Commands.Context;
using MyAvaloniaManagement.Business.Presentation;

namespace MyAvaloniaManagement.Business.Commands.Execution;

/// <summary>菜单和命令面板共用的宿主入口，显示或激活完成后立即归还命令执行权。</summary>
/// <remarks>不能等待非模态窗口的整个存续期，否则退出排空可能等待一个尚未关闭的诊断窗口。</remarks>
internal sealed class HostOpenPluginStatusCommandHandler(PluginStatusWindowService windows) : IHostWorkbenchCommandHandler
{
    public bool CanExecute(WorkbenchContextSnapshot context) => windows.CanShow;

    public async ValueTask ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            windows.ShowOrActivate();
        });
    }
}
