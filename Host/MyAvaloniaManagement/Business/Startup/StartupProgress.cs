using System;
using System.Collections.Generic;
using System.Linq;

namespace MyAvaloniaManagement.Business.Startup;

/// <summary>宿主启动阶段；阶段内计数互相独立，不解释为整个启动的时间百分比。</summary>
internal enum StartupStage { Preparing, Scanning, Loading, Registering, Validating, Initializing, Workbench, Ready }
internal enum StartupOutcome { Started, Succeeded, Failed, Skipped }

/// <summary>纯数据进度，不引用插件、容器或控件。Total 为 null 表示尚未知道总数。</summary>
internal sealed record StartupProgress(StartupStage Stage, string? PluginId = null,
    int Completed = 0, int? Total = null, StartupOutcome Outcome = StartupOutcome.Started,
    string? ErrorCode = null)
{
    internal long Sequence { get; init; }
}

/// <summary>加载边界只写入事实；界面通过快照读取，不在插件执行线程调用 UI 观察者。</summary>
internal interface IStartupProgressSink
{
    void Report(StartupProgress progress);
}

/// <summary>一次启动独占的有界展示缓冲。完整诊断仍由原日志拥有，这里只保留最近三项及失败摘要。</summary>
internal sealed class StartupProgressBuffer : IStartupProgressSink
{
    private readonly object _gate = new();
    private readonly List<StartupProgress> _recent = [];
    private readonly HashSet<string> _failures = new(StringComparer.Ordinal);
    private StartupProgress _current = new(StartupStage.Preparing);
    private bool _closed;
    private long _sequence;

    public void Report(StartupProgress progress)
    {
        lock (_gate)
        {
            if (_closed) return;
            _current = progress with { Sequence = ++_sequence };
            if (progress.Outcome == StartupOutcome.Failed)
                _failures.Add(progress.PluginId ?? progress.ErrorCode ?? progress.Stage.ToString());
            if (progress.PluginId is null) return;
            _recent.RemoveAll(item => item.Stage == progress.Stage && item.PluginId == progress.PluginId);
            _recent.Add(_current);
            if (_recent.Count > 3) _recent.RemoveAt(0);
        }
    }

    internal StartupProgressSnapshot Snapshot
    {
        get { lock (_gate) return new(_current, _recent.ToArray(), _failures.Count); }
    }

    /// <summary>封闭晚到报告；终态窗口关闭后，后台任务仍不能通过进度引用复活它。</summary>
    internal void Close() { lock (_gate) _closed = true; }
}

internal sealed record StartupProgressSnapshot(StartupProgress Current,
    IReadOnlyList<StartupProgress> Recent, int FailureCount);

/// <summary>进度是附加观察，不参与插件成败判定；第三方测试观察器抛出异常也不能被误记为插件失败。</summary>
internal static class StartupProgressReporting
{
    internal static void ReportSafely(this IStartupProgressSink? sink, StartupProgress progress)
    {
        try { sink?.Report(progress); }
        catch (Exception exception)
        { Console.Error.WriteLine($"Startup progress observer failed: {exception.GetType().Name}"); }
    }
}
