using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Models.Plugins;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.ViewModels.Tools;

/// <summary>
/// 展示当前启动会话中所有托管插件的加载和生命周期结果。
/// </summary>
internal sealed class PluginStatusViewModel
{
    public PluginStatusViewModel(
        PluginRegistry pluginRegistry,
        HostDiagnosticSession? diagnostics = null,
        PluginAvailabilityReadModel? availability = null)
    {
        ArgumentNullException.ThrowIfNull(pluginRegistry);

        availability ??= new PluginAvailabilityReadModel(
            new PluginLifecycleStateStore(pluginRegistry));
        Items = new ObservableCollection<PluginStatusItem>(
            CreateItems(pluginRegistry, diagnostics, availability));
    }

    public ObservableCollection<PluginStatusItem> Items { get; }

    private static IReadOnlyList<PluginStatusItem> CreateItems(
        PluginRegistry registry,
        HostDiagnosticSession? diagnostics,
        PluginAvailabilityReadModel availability)
    {
        var items = new List<PluginStatusItem>();

        foreach (var plugin in registry.Plugins
                     .OrderBy(item => item.Manifest.PluginId.Value, StringComparer.Ordinal))
        {
            var pluginId = plugin.Manifest.PluginId;
            items.Add(ToItem(
                pluginId.Value,
                plugin.EntryAssembly.GetName().Name ?? "未知程序集",
                plugin.Manifest,
                registry.Lifecycles.Any(item => item.OwnerId.Value == pluginId.Value),
                availability.GetLifecycleState(
                    new MyAvaloniaManagement.PluginSdk.PluginId(pluginId.Value))));
        }

        if (diagnostics is not null)
        {
            var rejectedCandidates = diagnostics.Snapshot
                .Where(item =>
                    item.PluginDirectory is not null &&
                    item.Phase is HostDiagnosticPhase.PluginManifestPreflight
                        or HostDiagnosticPhase.PluginRootDiscovery
                        or HostDiagnosticPhase.PluginAssemblyLoad
                        or HostDiagnosticPhase.PluginTypePreflight)
                .GroupBy(item => item.PluginDirectory!, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in rejectedCandidates)
            {
                var records = candidate.OrderBy(item => item.Sequence).ToArray();
                items.Add(new PluginStatusItem(
                    records.Select(item => item.PluginId)
                        .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id))
                    ?? $"目录：{candidate.Key}",
                    records.Select(item => item.AssemblyName)
                        .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "未完成加载",
                    records.Any(item => item.Phase == HostDiagnosticPhase.PluginManifestPreflight)
                        ? "兼容检查失败 · 未加载"
                        : "加载失败 · 已隔离",
                    "—",
                    "无",
                    string.Join(
                        Environment.NewLine,
                        records.Select(item =>
                            $"[{item.Code}] {ToPhaseText(item.Phase)}：{item.UserMessage}")))
                {
                    VersionText = records.Select(item => item.PluginVersion)
                        .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version))
                    ?? "未读取",
                    CompatibilityText = ToRejectedCompatibilityText(records),
                });
            }
        }

        return items
            .OrderBy(item => item.PluginId, StringComparer.Ordinal)
            .ToArray();
    }

    private static PluginStatusItem ToItem(
        string pluginId,
        string assemblyName,
        PluginManifest? manifest,
        bool hasLifecycle,
        PluginLifecycleState? lifecycleState)
    {
        var version = manifest is null
            ? "未提供"
            : PluginVersionText.Format(manifest.PluginVersion);
        var compatibility = manifest is null
            ? "未通过清单发现入口"
            : $"Plugin SDK {manifest.Sdk}";
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

    private static string ToPhaseText(HostDiagnosticPhase phase) => phase switch
    {
        HostDiagnosticPhase.PluginRootDiscovery => "目录发现",
        HostDiagnosticPhase.PluginManifestPreflight => "兼容预检",
        HostDiagnosticPhase.PluginAssemblyLoad => "程序集加载",
        HostDiagnosticPhase.PluginTypePreflight => "类型预检",
        HostDiagnosticPhase.PluginModuleDiscovery => "模块发现",
        HostDiagnosticPhase.PluginServiceRegistration => "服务注册",
        HostDiagnosticPhase.ExtensionDiscovery => "扩展组合",
        HostDiagnosticPhase.PluginLifecycle => "生命周期",
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
