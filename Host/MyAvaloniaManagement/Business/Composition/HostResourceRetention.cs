using System.Collections.Generic;

namespace MyAvaloniaManagement.Business.Composition;

/// <summary>
/// 故障对象图的最小进程期所有者，只持有强引用，不解析服务、不定时重试、不自动恢复或 Dispose。
/// 启动失败窗口可能长时间存在，因此不能把保留责任放在将要离开栈的局部变量上。
/// 测试使用独立实例，解除假任务后自行按测试协议清理，不能污染生产进程保留表。
/// </summary>
internal sealed class HostResourceRetention
{
    internal static HostResourceRetention ProcessLifetime { get; } = new();
    private readonly object _gate = new();
    private readonly List<HostRuntimeShutdown> _retained = [];

    internal int Count { get { lock (_gate) return _retained.Count; } }

    internal void Retain(HostRuntimeShutdown ownership)
    {
        lock (_gate)
        {
            if (!_retained.Contains(ownership)) _retained.Add(ownership);
        }
    }
}
