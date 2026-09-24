using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MyAvaloniaManagement.Business.PluginStatus;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Installation;
using MyAvaloniaManagement.Models.Plugins;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.ViewModels.PluginStatus;
using MyAvaloniaManagement.Views.PluginStatus;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>只替换安装用例边界，验证真实看板绑定。文件事务和进程行为另由真实磁盘/Host 专项覆盖。</summary>
public sealed class PluginInstallationWindowTests
{
    [AvaloniaTheory]
    [InlineData((int)PluginInstallAction.Install, false)]
    [InlineData((int)PluginInstallAction.Upgrade, false)]
    [InlineData((int)PluginInstallAction.Reinstall, true)]
    [InlineData((int)PluginInstallAction.Downgrade, true)]
    [InlineData((int)PluginInstallAction.Restore, true)]
    public async Task 空看板也能预览且替换动作必须明确确认(int action, bool explicitChoice)
    {
        var service = new Actions((PluginInstallAction)action);
        using var model = new PluginStatusWindowViewModel(new EmptyQuery(), TimeProvider.System, installation: service);
        model.Refresh();
        var window = new PluginStatusWindow { DataContext = model };
        window.Show();
        try
        {
            await UiTestWait.RenderAsync();
            Assert.True(model.HasNoResults);
            Assert.True(window.FindControl<Button>("InstallZipButton")!.IsVisible);
            await model.InspectPackageAsync("中文 包.zip");
            Assert.Contains("未进行发布摘要对照", model.PackagePreviewText);
            Assert.Contains("候选版本：2.0.0", model.PackagePreviewText);
            Assert.Equal(!explicitChoice, model.CanCommitPackage);
            model.AllowPackageReplacement = true;
            await UiTestWait.RenderAsync();
            Assert.True(window.FindControl<Button>("CommitPackageButton")!.IsEnabled);
            await model.CommitPackageCommand.ExecuteAsync(null);
            Assert.True(model.HasPendingRestart);
            Assert.True(model.CanCancelPendingInstall);
            Assert.False(model.CanCommitPackage);
            await model.CancelPendingInstallCommand.ExecuteAsync(null);
            Assert.False(model.HasPendingRestart);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task 检查忙碌时禁止重复操作并可显式取消()
    {
        var service = new Actions(PluginInstallAction.Install) { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var model = new PluginStatusWindowViewModel(new EmptyQuery(), TimeProvider.System, installation: service);
        var checking = model.InspectPackageAsync("package.zip");
        Assert.True(model.IsInstallationBusy); Assert.False(model.CanInspectPackage); Assert.False(model.CanCommitPackage);
        model.CancelPackageInspectionCommand.Execute(null);
        await checking;
        Assert.False(model.IsInstallationBusy); Assert.Contains("已取消", model.InstallationFeedback);
        Assert.Null(service.Preview); Assert.False(model.HasPendingRestart);
    }

    [AvaloniaFact]
    public async Task 窗口释放不取消已接受提交且迟到结果不再更新窗口()
    {
        var service = new Actions(PluginInstallAction.Install);
        var model = new PluginStatusWindowViewModel(new EmptyQuery(), TimeProvider.System, installation: service);
        await model.InspectPackageAsync("package.zip");
        service.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var committing = model.CommitPackageCommand.ExecuteAsync(null);
        var feedback = model.InstallationFeedback;
        model.Dispose(); service.Hold.SetResult(); await committing;
        Assert.True(service.Status.CanCancel); Assert.Equal(feedback, model.InstallationFeedback);
        using var reopened = new PluginStatusWindowViewModel(new EmptyQuery(), TimeProvider.System, installation: service);
        await reopened.RefreshInstallationAsync();
        Assert.True(reopened.HasPendingRestart); Assert.True(reopened.CanCancelPendingInstall);
    }

    [AvaloniaFact]
    public async Task 受控失败显示中文而底层异常不泄露路径()
    {
        var service = new Actions(PluginInstallAction.Install) { Failure = new PluginInstallException("bad", "配套清单不一致。") };
        using var model = new PluginStatusWindowViewModel(new EmptyQuery(), TimeProvider.System, installation: service);
        await model.InspectPackageAsync("package.zip"); Assert.Equal("配套清单不一致。", model.InstallationFeedback);
        service.Failure = new IOException("private-secret-path");
        await model.InspectPackageAsync("package.zip"); Assert.DoesNotContain("private-secret-path", model.InstallationFeedback);
        Assert.False(model.HasPendingRestart); Assert.False(model.CanCommitPackage);
    }

    private sealed class EmptyQuery : IPluginStatusQuery
    { public IReadOnlyList<PluginStatusItem> Capture() => []; }

    private sealed class Actions(PluginInstallAction action) : IPluginInstallationActions
    {
        public PluginInstallationStatus Status { get; private set; } = new(null, "尚无待办");
        public PluginInstallPreview? Preview { get; private set; }
        internal TaskCompletionSource? Hold { get; set; }
        internal Exception? Failure { get; set; }
        public async Task InspectAsync(string zip, string? sidecar)
        {
            if (Hold is not null) await Hold.Task;
            if (Failure is not null) throw Failure;
            var manifest = new PluginManifest(2, new PluginId("myavalonia.plugin.ui-probe"), new(2, 0, 0),
                new("Probe.dll", "Probe.Module"), new(new(3, 0, 0), new(4, 0, 0)));
            Preview = new(new(Guid.NewGuid().ToString("N"), "Probe", "unused", manifest, new('A', 64), new('B', 64), false),
                new(action, "Probe", action == PluginInstallAction.Install ? null : "1.0.0", null,
                    action is PluginInstallAction.Reinstall or PluginInstallAction.Downgrade or PluginInstallAction.Restore));
        }
        public void CancelInspection() => Hold?.TrySetCanceled();
        public async Task CommitAsync(bool explicitReplacement)
        {
            if (Hold is not null) await Hold.Task;
            var preview = Preview!;
            Status = new(new()
            {
                SchemaVersion = 1, OperationId = preview.Package.OperationId, Action = action, Phase = PluginInstallPhase.Staged,
                PluginId = preview.PluginId, TargetDirectory = "Probe", PreviousVersion = preview.Plan.PreviousVersion,
                PreviousHash = null, Version = "2.0.0", ArtifactHash = new('B', 64), ArchiveHash = new('A', 64),
                HasReleaseManifest = false, PreviousRecord = null, OwnerPid = null, OwnerStartedUtcTicks = null, Result = null
            }, "待重启应用");
            Preview = null;
        }
        public Task CancelPendingAsync() { Status = new(null, "已取消"); return Task.CompletedTask; }
        public Task PrepareRestoreAsync(string pluginId) => Task.CompletedTask;
        public Task RefreshAsync() => Task.CompletedTask;
    }
}
