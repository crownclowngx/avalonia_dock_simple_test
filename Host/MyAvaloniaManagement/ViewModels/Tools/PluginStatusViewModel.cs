using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Models.Plugins;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.ViewModels.Tools;

/// <summary>
/// 展示当前启动会话中所有托管插件的加载和生命周期结果。
/// </summary>
internal sealed class PluginStatusViewModel : Tool
{
    internal PluginStatusViewModel(
        PluginRegistry pluginRegistry,
        PluginLifecycleManager lifecycleManager,
        HostDiagnosticSession? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(pluginRegistry);
        ArgumentNullException.ThrowIfNull(lifecycleManager);

        Id = HostExtensionIds.PluginStatus.Value;
        Title = "插件状态";
        CanClose = true;
        Items = new ObservableCollection<PluginStatusItem>(
            CreateItems(pluginRegistry, lifecycleManager, diagnostics));
    }

    public ObservableCollection<PluginStatusItem> Items { get; }

    private static IReadOnlyList<PluginStatusItem> CreateItems(
        PluginRegistry registry,
        PluginLifecycleManager manager,
        HostDiagnosticSession? diagnostics)
    {
        var items = new List<PluginStatusItem>();
        var moduleIds = new HashSet<PluginId>();

        foreach (var plugin in registry.Plugins
                     .OrderBy(item => item.Manifest.PluginId.Value, StringComparer.Ordinal))
        {
            var pluginId = plugin.Manifest.PluginId;
            moduleIds.Add(pluginId);
            items.Add(ToItem(
                pluginId.Value,
                plugin.EntryAssembly.GetName().Name ?? "未知程序集",
                plugin.Manifest,
                manager.GetState(pluginId)));
        }

        foreach (var state in manager.States
                     .Where(state => !moduleIds.Contains(state.PluginId)))
        {
            items.Add(ToItem(
                state.PluginId.Value,
                "未关联托管模块",
                manifest: null,
                state: state));
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
        PluginLifecycleState? state)
    {
        var version = manifest is null
            ? "未提供"
            : PluginVersionText.Format(manifest.PluginVersion);
        var compatibility = manifest is null
            ? "未通过清单发现入口"
            : $"Host API {manifest.HostApi}；Common {manifest.CommonContract}";
        if (state is null)
        {
            return new PluginStatusItem(
                pluginId,
                assemblyName,
                "已加载 · 无需后台生命周期",
                "—",
                "无",
                "插件模块已完成服务注册，没有需要宿主管理的后台启动或关闭操作。")
            {
                VersionText = version,
                CompatibilityText = compatibility,
            };
        }

        var dependencies = state.RequiredPluginIds.Count == 0
            ? "无"
            : string.Join("、", state.RequiredPluginIds);
        var duration = state.Duration is null
            ? "—"
            : $"{state.Duration.Value.TotalMilliseconds:0} ms";
        var detail = state.ErrorMessage;
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = state.Status switch
            {
                PluginLifecycleStatus.Ready => "插件后台服务已成功初始化。",
                PluginLifecycleStatus.Stopped => "插件后台服务已正常关闭。",
                PluginLifecycleStatus.NotStarted => "插件后台服务尚未初始化。",
                PluginLifecycleStatus.Initializing => "正在初始化插件后台服务。",
                PluginLifecycleStatus.Stopping => "正在关闭插件后台服务。",
                _ => "插件生命周期没有提供更多诊断信息。",
            };
        }

        if (!string.IsNullOrWhiteSpace(state.ErrorCode))
        {
            detail = $"[{state.ErrorCode}] {detail}";
        }

        return new PluginStatusItem(
            pluginId,
            assemblyName,
            ToStatusText(state.Status),
            duration,
            dependencies,
            detail)
        {
            VersionText = version,
            CompatibilityText = compatibility,
        };
    }

    private static string ToStatusText(PluginLifecycleStatus status) => status switch
    {
        PluginLifecycleStatus.NotStarted => "未启动",
        PluginLifecycleStatus.Initializing => "初始化中",
        PluginLifecycleStatus.Ready => "运行正常",
        PluginLifecycleStatus.Blocked => "依赖阻塞",
        PluginLifecycleStatus.Failed => "执行失败",
        PluginLifecycleStatus.TimedOut => "执行超时 · 建议重启",
        PluginLifecycleStatus.Stopping => "关闭中",
        PluginLifecycleStatus.Stopped => "已关闭",
        _ => status.ToString(),
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
        var hostRange = records.Select(item => item.HostApiRange)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var commonRange = records.Select(item => item.CommonContractRange)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var current = HostCompatibilityProfile.Current;
        return
            $"Host API {hostRange ?? "未声明"}（当前 {PluginVersionText.Format(current.HostApiVersion)}）；" +
            $"Common {commonRange ?? "未声明"}（当前 {PluginVersionText.Format(current.CommonContractVersion)}）";
    }
}
