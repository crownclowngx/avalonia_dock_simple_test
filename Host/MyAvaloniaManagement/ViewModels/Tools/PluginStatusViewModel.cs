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
    public PluginStatusViewModel(
        PluginRegistry pluginRegistry,
        HostDiagnosticSession? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(pluginRegistry);

        Id = HostExtensionIds.PluginStatus.Value;
        Title = "插件状态";
        CanClose = true;
        Items = new ObservableCollection<PluginStatusItem>(
            CreateItems(pluginRegistry, diagnostics));
    }

    public ObservableCollection<PluginStatusItem> Items { get; }

    private static IReadOnlyList<PluginStatusItem> CreateItems(
        PluginRegistry registry,
        HostDiagnosticSession? diagnostics)
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
                registry.Lifecycles.Any(item =>
                    item.OwnerId.Value == pluginId.Value)));
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
        bool hasLifecycle)
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
                "无",
                "插件模块已完成服务注册，没有需要宿主管理的后台启动或关闭操作。")
            {
                VersionText = version,
                CompatibilityText = compatibility,
            };
        }

        return new PluginStatusItem(
            pluginId,
            assemblyName,
            "生命周期已声明 · G8 前不执行",
            "—",
            "G8 尚未编排",
            "G5 已验证生命周期 singleton 可解析；初始化、关闭、超时和状态机由 G8 实现。")
        {
            VersionText = version,
            CompatibilityText = compatibility,
        };
    }

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
