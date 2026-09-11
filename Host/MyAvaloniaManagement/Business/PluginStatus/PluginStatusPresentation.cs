using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Models.Plugins;

namespace MyAvaloniaManagement.Business.PluginStatus;

/// <summary>集中维护状态中文文案；查询只组合事实，窗口只绑定结果。</summary>
/// <remarks>沿用原有生命周期含义，避免迁移窗口时改变“加载成功”和“贡献可用”的判断。</remarks>
internal static class PluginStatusPresentation
{
    internal static PluginStatusItem ForPlugin(PluginRegistryPlugin plugin, bool hasLifecycle, PluginLifecycleState? state) =>
        ToItem(plugin.Manifest.PluginId.Value, plugin.EntryAssembly.GetName().Name ?? "未知程序集",
            plugin.Manifest, hasLifecycle, state);

    internal static PluginStatusItem ForRejectedCandidate(IReadOnlyList<HostDiagnosticRecord> records)
    {
        var id = records.Select(item => item.PluginId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var directory = records.Select(item => item.PluginDirectory).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return new PluginStatusItem(id ?? $"目录：{directory}",
            records.Select(item => item.AssemblyName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "未完成加载",
            records.Any(item => item.Phase == HostDiagnosticPhase.PluginManifestPreflight)
                ? "兼容检查失败 · 未加载" : "加载失败 · 已隔离", "—", "无",
            string.Join(Environment.NewLine, records.Select(item => $"[{item.Code}] {PhaseText(item.Phase)}：{item.UserMessage}")))
        {
            VersionText = records.Select(item => item.PluginVersion).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "未读取",
            CompatibilityText = ToRejectedCompatibilityText(records)
        };
    }

    private static PluginStatusItem ToItem(
        string pluginId,
        string assemblyName,
        PluginManifest manifest,
        bool hasLifecycle,
        PluginLifecycleState? lifecycleState)
    {
        var version = PluginVersionText.Format(manifest.PluginVersion);
        var compatibility = $"Plugin SDK {manifest.Sdk}";
        if (!hasLifecycle)
        {
            return new PluginStatusItem(
                pluginId,
                assemblyName,
                "已加载 · 无需后台生命周期",
                "—",
                "可用",
                "插件模块已完成服务注册，没有需要宿主管理的后台启动或关闭操作。")
            {
                VersionText = version,
                CompatibilityText = compatibility,
            };
        }

        lifecycleState ??= new PluginLifecycleState(
            new MyAvaloniaManagement.PluginSdk.PluginId(pluginId),
            PluginLifecycleStatus.NotStarted);
        var duration = lifecycleState.Duration is { } elapsed
            ? $"{elapsed.TotalMilliseconds:0.###} ms"
            : "—";
        var presentation = ToLifecyclePresentation(lifecycleState);
        return new PluginStatusItem(
            pluginId,
            assemblyName,
            presentation.Status,
            duration,
            presentation.Availability,
            presentation.Detail)
        {
            VersionText = version,
            CompatibilityText = compatibility,
        };
    }

    private static (string Status, string Availability, string Detail)
        ToLifecyclePresentation(PluginLifecycleState state) => state.Status switch
        {
            PluginLifecycleStatus.NotStarted => (
                "等待生命周期初始化",
                "尚不可用",
                "宿主尚未执行该插件的初始化回调。"),
            PluginLifecycleStatus.Initializing => (
                "正在初始化",
                "尚不可用",
                "插件贡献将在初始化完整成功后统一开放。"),
            PluginLifecycleStatus.Ready => (
                "生命周期初始化成功",
                "可用",
                "插件后台资源与贡献均已进入可用状态。"),
            PluginLifecycleStatus.InitializationFailed => (
                "生命周期初始化失败",
                "已隔离",
                $"[{state.ErrorCode}] 插件贡献未进入菜单、布局或创建流程。"),
            PluginLifecycleStatus.InitializationTimedOut => (
                "生命周期初始化超时",
                "已隔离",
                $"[{state.ErrorCode}] 宿主已请求取消，其他插件继续启动。"),
            PluginLifecycleStatus.HostCancelled => (
                "生命周期被宿主取消",
                "已隔离",
                $"[{state.ErrorCode}] 初始化调度已经停止。"),
            PluginLifecycleStatus.Stopping => (
                "正在停止",
                "正在退出",
                "宿主正在按成功启动顺序的反向停止插件。"),
            PluginLifecycleStatus.Stopped => (
                "生命周期已停止",
                "已停止",
                "插件后台资源已停止使用，即将释放私有 Provider。"),
            PluginLifecycleStatus.ShutdownFailed => (
                "生命周期停止失败",
                "正在退出",
                $"[{state.ErrorCode}] 宿主仍会继续释放其他插件和 Provider。"),
            PluginLifecycleStatus.ShutdownTimedOut => (
                "生命周期停止超时",
                "正在退出",
                $"[{state.ErrorCode}] 宿主已请求取消并继续退出。"),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    internal static string PhaseText(HostDiagnosticPhase phase) => phase switch
    {
        HostDiagnosticPhase.PluginRootDiscovery => "目录发现",
        HostDiagnosticPhase.PluginManifestPreflight => "兼容预检",
        HostDiagnosticPhase.PluginAssemblyLoad => "程序集加载",
        HostDiagnosticPhase.PluginTypePreflight => "类型预检",
        HostDiagnosticPhase.PluginModuleDiscovery => "模块发现",
        HostDiagnosticPhase.PluginServiceRegistration => "服务注册",
        HostDiagnosticPhase.HostContainerBuild => "容器构建",
        HostDiagnosticPhase.ExtensionDiscovery => "扩展组合",
        HostDiagnosticPhase.PluginLifecycle => "生命周期",
        HostDiagnosticPhase.WorkflowAction => "工作流动作",
        HostDiagnosticPhase.WorkbenchCommand => "工作台命令",
        HostDiagnosticPhase.IconPresentation => "图标展示",
        _ => phase.ToString(),
    };

    private static string ToRejectedCompatibilityText(
        IReadOnlyList<HostDiagnosticRecord> records)
    {
        var sdkRange = records.Select(item => item.SdkRange)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var current = PluginSdkCompatibilityProfile.Current;
        return $"Plugin SDK {sdkRange ?? "未声明"}（当前 {PluginVersionText.Format(current.SdkVersion)}）";
    }
}
