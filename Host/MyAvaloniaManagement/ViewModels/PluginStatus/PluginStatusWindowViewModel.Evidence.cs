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
    [ObservableProperty] private string _artifactText = "选择插件后查看产物信息。";
    [ObservableProperty] private string _buildText = "编译包版本未知；不从 AssemblyVersion 推断 NuGet 版本。";
    [ObservableProperty] private string _referenceText = "尚未读取程序集引用。";
    [ObservableProperty] private string _suggestionText = "可先查看当前会话诊断，再按需导入验收报告。";
    [ObservableProperty] private IReadOnlyList<CompatibilityEvidenceRow> _evidenceRows = [];
    [ObservableProperty] private IReadOnlyList<CompatibilityMatrixColumn> _matrixColumns = [];
    [ObservableProperty] private IReadOnlyList<CompatibilityMatrixRow> _matrixRows = [];

    public bool HasNoEvidence => EvidenceRows.Count == 0;
    public bool HasNoMatrix => MatrixColumns.Count == 0;
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
                ? $"Host {host.ProductVersion} · {host.InformationalVersion}\nSDK {host.SdkVersion} · Avalonia {host.AvaloniaVersion} · Dock {host.DockVersion}\n{host.OperatingSystem} / {host.Architecture} · {host.Framework}\n运行时 {host.RuntimeHash}\n规则 {host.RuleHash}"
                : "Host 文件身份尚未确认；已有报告保持历史参考状态。";
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
        var matrix = CompatibilityDashboardProjection.CreateMatrix(_snapshot, VisibleItems.Select(item => item.PluginId));
        MatrixColumns = matrix.Columns;
        MatrixRows = matrix.Rows;
        OnPropertyChanged(nameof(HasNoMatrix));
    }

    private void UpdateEvidenceSelection()
    {
        var id = SelectedItem?.PluginId;
        var inspection = _snapshot?.Artifacts.FirstOrDefault(item => item.PluginId == id);
        var artifact = inspection?.Artifact;
        ArtifactText = inspection?.State ?? "尚未检查，或该候选没有已加载的可检查入口；当前会话诊断仍可查看。";
        if (artifact is not null) ArtifactText += $"\n产物 {artifact.Identity.Sha256}\n共 {artifact.Identity.Files.Count} 个文件；声明 SDK {artifact.SdkRange}";
        BuildText = artifact?.Build is { } build
            ? $"目标 {build.TargetFramework} · Build {build.BuildVersion}\n" + string.Join("\n", build.Packages.Select(package => $"{package.Id} {package.Version}"))
            : "编译 NuGet 包版本未知（旧包可能没有 plugin.build.json）；不从程序集版本推断。";
        ReferenceText = artifact is null ? "尚未读取程序集引用。" : "程序集引用身份（AssemblyVersion）：\n" +
            string.Join("\n", artifact.AssemblyReferences.Select(reference => $"{reference.Id} {reference.Version}"));
        EvidenceRows = _snapshot is null || id is null ? [] : CompatibilityDashboardProjection.Evidence(_snapshot, id);
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
