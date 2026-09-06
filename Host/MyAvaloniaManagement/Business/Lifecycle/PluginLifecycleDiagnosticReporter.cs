using System;
using MyAvaloniaManagement.Business.Diagnostics;

namespace MyAvaloniaManagement.Business.Lifecycle;

/// <summary>
/// 生命周期迟到报告的关闭屏障。锁只保护 Host 诊断出口，不执行插件代码；Close 返回时既有报告已经结束，
/// 后续后台通知不会再访问由 Program 独立拥有的诊断会话。
/// </summary>
internal sealed class PluginLifecycleDiagnosticReporter(IHostDiagnosticSink? sink)
{
    private readonly object _gate = new();
    private bool _closed;

    internal void Report(HostDiagnosticDraft draft)
    {
        lock (_gate)
        {
            if (_closed) return;
            try { sink?.Report(draft); }
            catch (Exception) { /* 诊断设施失败不能改变任务终态或导致错误释放。 */ }
        }
    }

    internal void Close()
    {
        lock (_gate) { _closed = true; }
    }
}
