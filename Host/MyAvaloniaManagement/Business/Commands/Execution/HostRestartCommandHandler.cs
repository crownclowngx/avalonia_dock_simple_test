using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Commands.Context;
using MyAvaloniaManagement.Business.Restart;

namespace MyAvaloniaManagement.Business.Commands.Execution;

/// <summary>只投递重启请求并同步归还命令租约，不等待包含自身的退出排空。</summary>
/// <remarks>投递后不保留命令取消令牌；请求由协调器和窗口的正常关闭确认负责，避免执行器关闭时反向取消交接。</remarks>
internal sealed class HostRestartCommandHandler(IHostRestartActions restart) : IHostWorkbenchCommandHandler
{
    public bool CanExecute(WorkbenchContextSnapshot context) => restart.CanRequest;
    public ValueTask ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dispatcher.UIThread.Post(restart.Request, DispatcherPriority.Background);
        return ValueTask.CompletedTask;
    }
}
