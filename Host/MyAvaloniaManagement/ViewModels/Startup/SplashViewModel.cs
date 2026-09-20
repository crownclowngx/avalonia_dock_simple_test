using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MyAvaloniaManagement.Business.Startup;

namespace MyAvaloniaManagement.ViewModels.Startup;

/// <summary>只解释启动快照的展示含义；插件可用性、超时和继续启动政策都由既有宿主流程决定。</summary>
internal sealed class SplashViewModel : ObservableObject
{
    private StartupProgressSnapshot _snapshot = new(new(StartupStage.Preparing), [], 0);
    private bool _stopping;
    public string Title => _stopping ? "正在停止启动" : "正在启动工作台";
    public string StageText => _stopping ? "正在等待当前操作安全结束…" : StageLabel(_snapshot.Current.Stage);
    public string PluginText => _snapshot.Current.PluginId ?? "请稍候，即将为你打开工作台";
    public bool IsIndeterminate => _snapshot.Current.Total is not > 0;
    public double ProgressValue => _snapshot.Current.Total is > 0
        ? 100d * _snapshot.Current.Completed / _snapshot.Current.Total.Value : 0;
    public string CountText => _snapshot.Current.Total is > 0
        ? $"本阶段已处理 {_snapshot.Current.Completed} / {_snapshot.Current.Total} 项" : "正在准备…";
    public string RecentText => string.Join("\n", _snapshot.Recent.Select(item =>
        $"{(item.Outcome switch { StartupOutcome.Succeeded => "✓", StartupOutcome.Failed => "!", StartupOutcome.Skipped => "–", _ => "◌" })} {item.PluginId} · {OutcomeLabel(item.Outcome)}"));
    public string FailureText => _snapshot.FailureCount == 0 ? "" : $"有 {_snapshot.FailureCount} 项未成功，详情将保留在插件看板";

    internal void Apply(StartupProgressSnapshot snapshot)
    {
        if (_snapshot.Current.Sequence >= snapshot.Current.Sequence) return;
        _snapshot = snapshot;
        Refresh();
    }

    internal void Stop() { _stopping = true; Refresh(); }
    private void Refresh()
    {
        foreach (var property in new[] { nameof(Title), nameof(StageText), nameof(PluginText), nameof(IsIndeterminate),
                     nameof(ProgressValue), nameof(CountText), nameof(RecentText), nameof(FailureText) })
            OnPropertyChanged(property);
    }

    private static string OutcomeLabel(StartupOutcome outcome) => outcome switch
    { StartupOutcome.Succeeded => "本阶段完成", StartupOutcome.Failed => "失败", StartupOutcome.Skipped => "已跳过", _ => "处理中" };
    internal static string StageLabel(StartupStage stage) => stage switch
    {
        StartupStage.Preparing => "正在读取启动配置", StartupStage.Scanning => "正在扫描插件目录",
        StartupStage.Loading => "正在检查并加载插件", StartupStage.Registering => "正在注册插件",
        StartupStage.Validating => "正在检查插件贡献", StartupStage.Initializing => "正在初始化插件",
        StartupStage.Workbench => "正在准备主界面", _ => "工作台已就绪",
    };
}
