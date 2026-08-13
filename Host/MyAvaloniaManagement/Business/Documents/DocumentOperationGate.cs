using System;
using System.Threading;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>
/// 为打开、菜单保存和关闭前保存提供同一串行边界。
/// </summary>
/// <remarks>
/// 同一进程只有一个主工作区。使用一个窄门比在多个协调器中分别加锁更重要：否则标签
/// 关闭保存可能与菜单保存同时覆盖文件，批量打开也可能在保存期间观察到半更新的路径状态。
/// </remarks>
internal sealed class DocumentOperationGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await _gate.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            _gate.Release();
        }
    }
}
