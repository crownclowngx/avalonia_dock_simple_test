using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagement.Business.Compatibility;

namespace MyAvaloniaManagement.ViewModels.PluginStatus;

/// <summary>窗口拥有读取任务；代次只控制展示，报告存储和插件生命周期仍由各自服务拥有。</summary>
internal sealed partial class PluginStatusWindowViewModel
{
    private readonly IPluginDashboardEvidence? _evidence;
    private CancellationTokenSource? _reading;
    private int _generation;
    private bool _disposed;
    private PluginDashboardSnapshot? _snapshot;

    [ObservableProperty] private bool _isInspecting;
    [ObservableProperty] private string _evidenceNotice = "尚未读取验收报告；无报告不等于不兼容。";
    [ObservableProperty] private string _evidenceTimeText = "尚未检查安装产物";
    [ObservableProperty] private string _hostText = "Host 文件身份尚未检查。";
    [ObservableProperty] private string _hostIdentityText = "尚未取得 Host 文件身份。";
    [ObservableProperty] private string _artifactText = "选择插件后查看产物信息。";
    [ObservableProperty] private string _artifactSummaryText = "尚未检查安装产物。";
    [ObservableProperty] private string _currentEvidenceText = "尚无匹配当前环境的验收证据。";
    [ObservableProperty] private bool _showHistory;
    [ObservableProperty] private string _selectedEvidenceFilter = "全部证据";
    [ObservableProperty] private string _buildText = "编译包版本未知；不从 AssemblyVersion 推断 NuGet 版本。";
    [ObservableProperty] private string _buildSdkText = "未知（未提供编译包信息）";
    [ObservableProperty] private string _referenceText = "尚未读取程序集引用。";
    [ObservableProperty] private string _suggestionText = "可先查看当前会话诊断，再按需导入验收报告。";
    [ObservableProperty] private IReadOnlyList<CompatibilityEvidenceRow> _evidenceRows = [];
    [ObservableProperty] private IReadOnlyList<CompatibilityMatrixColumn> _matrixColumns = [];
    [ObservableProperty] private IReadOnlyList<CompatibilityMatrixRow> _matrixRows = [];

    public bool HasNoEvidence => EvidenceRows.Count == 0;
    public bool HasNoMatrix => MatrixColumns.Count == 0;
    public bool HasNoMatrixRows => MatrixColumns.Count > 0 && MatrixRows.Count == 0;
    public IReadOnlyList<string> EvidenceFilters { get; } = ["全部证据", "匹配当前", "存在失败", "有未执行", "不匹配 / 待检查", "无报告"];
    partial void OnShowHistoryChanged(bool value) => UpdateMatrix();
    partial void OnSelectedEvidenceFilterChanged(string value) => UpdateMatrix();
    public bool CanInspect => !_disposed && !IsInspecting && _evidence is not null;
    partial void OnIsInspectingChanged(bool value) => OnPropertyChanged(nameof(CanInspect));

    [RelayCommand]
    public Task CheckArtifactsAsync() => ReadEvidenceAsync(true);

    public Task LoadEvidenceAsync() => ReadEvidenceAsync(false);
    public Task ImportReportAsync(string path) => ReadEvidenceAsync(false, path);

    private async Task ReadEvidenceAsync(bool inspect, string? importPath = null)
    {
        if (_disposed || _evidence is null) return;
        _reading?.Cancel();
        var source = new CancellationTokenSource();
        _reading = source;
        var generation = ++_generation;
        IsInspecting = true;
        try
        {
            if (importPath is not null) await _evidence.ImportAsync(importPath, source.Token);
            var snapshot = await _evidence.ReadAsync(inspect, source.Token);
            // 即使基础设施忽略取消，迟到任务也不能覆盖新结果或写回已经关闭的窗口。
            if (_disposed || generation != _generation || source.IsCancellationRequested) return;
            _snapshot = snapshot;
            EvidenceNotice = snapshot.Notice + (snapshot.InvalidReports > 0 ? $" 已忽略 {snapshot.InvalidReports} 份损坏报告。" : "");
            EvidenceTimeText = snapshot.CheckedAtUtc is { } at ? $"文件检查于 {at.ToLocalTime():yyyy-MM-dd HH:mm:ss}；后续变化需重新检查" : "尚未检查安装产物";
            HostText = snapshot.Host is { } host
                ? $"Host {host.ProductVersion}\nSDK {host.SdkVersion} · Avalonia {host.AvaloniaVersion} · Dock {host.DockVersion}\n{host.OperatingSystem} / {host.Architecture} · {host.Framework}"
                : "Host 文件身份尚未确认；已有报告保持历史参考状态。";
            HostIdentityText = snapshot.Host is { } identity
                ? $"构建 {identity.InformationalVersion}\n运行时 {identity.RuntimeHash}\n规则 {identity.RuleHash}"
                : "尚未取得 Host 文件身份。";
            ErrorMessage = string.Empty;
            UpdateMatrix();
            UpdateEvidenceSelection();
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_disposed && generation == _generation)
                ErrorMessage = "证据读取或导入失败；保留上一次快照及其时间，请检查文件格式、权限后重试。";
        }
        finally
        {
            if (!_disposed && generation == _generation) { IsInspecting = false; _reading = null; }
            source.Dispose();
        }
    }

    private void UpdateMatrix()
    {
        if (_snapshot is null) return;
        var matrix = CompatibilityDashboardProjection.CreateMatrix(_snapshot, VisibleItems.Select(item => item.PluginId), SelectedEvidenceFilter, ShowHistory);
        MatrixColumns = matrix.Columns;
        MatrixRows = matrix.Rows;
        OnPropertyChanged(nameof(HasNoMatrix));
        OnPropertyChanged(nameof(HasNoMatrixRows));
    }

    private void UpdateEvidenceSelection()
    {
        var id = SelectedItem?.PluginId;
        var inspection = _snapshot?.Artifacts.FirstOrDefault(item => item.PluginId == id);
        var artifact = inspection?.Artifact;
        ArtifactText = inspection?.State ?? "尚未检查，或该候选没有已加载的可检查入口；当前会话诊断仍可查看。";
        ArtifactSummaryText = ArtifactText;
        if (artifact is not null) ArtifactText += $"\n产物 {artifact.Identity.Sha256}\n共 {artifact.Identity.Files.Count} 个文件；声明 SDK {artifact.SdkRange}";
        BuildText = artifact?.Build is { } build
            ? $"目标 {build.TargetFramework} · Build {build.BuildVersion}\n" + string.Join("\n", build.Packages.Select(package => $"{package.Id} {package.Version}"))
            : "编译 NuGet 包版本未知（旧包可能没有 plugin.build.json）；不从程序集版本推断。";
        BuildSdkText = artifact?.Build?.Packages.FirstOrDefault(package => package.Id == "MyAvaloniaManagement.PluginSdk")?.Version
            ?? "未知（未提供 SDK 编译包版本；源码引用不是 NuGet 包）";
        ReferenceText = artifact is null ? "尚未读取程序集引用。" : "程序集引用身份（AssemblyVersion）：\n" +
            string.Join("\n", artifact.AssemblyReferences.Select(reference => $"{reference.Id} {reference.Version}"));
        EvidenceRows = _snapshot is null || id is null ? [] : CompatibilityDashboardProjection.Evidence(_snapshot, id);
        var matched = _snapshot?.Reports.Where(item => item.Report.PluginId == id &&
            CompatibilityMatcher.Match(item.Report, artifact, _snapshot.Host) == EvidenceMatch.Matches)
            .OrderByDescending(item => item.Report.ExecutedAtUtc).FirstOrDefault();
        CurrentEvidenceText = matched is null ? "尚无匹配当前产物与环境的报告；历史记录见详情。" :
            "匹配当前检查：\n" + CompatibilityMatcher.Summary(matched.Report).Replace("；", "\n");
        SuggestionText = SelectedItem?.HasProblem == true ? "先查看本次会话的异常诊断；历史通过报告不能覆盖当前失败。" :
            artifact is null ? "检查安装产物以确定报告是否适用；文件已变化时先重启 Host。" :
            EvidenceRows.Count == 0 ? "当前没有验收报告；可用性来自本次会话，尚无离线验证证据。" :
            "结合适用性和各层级结果判断；未执行的业务与真机场景仍需补验。";
        OnPropertyChanged(nameof(HasNoEvidence));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_generation;
        _reading?.Cancel();
        // 任务在 finally 中释放自身 CTS，避免异步 I/O 尚未观察取消就被窗口提前释放。
        _reading = null;
        OnPropertyChanged(nameof(CanInspect));
    }
}
