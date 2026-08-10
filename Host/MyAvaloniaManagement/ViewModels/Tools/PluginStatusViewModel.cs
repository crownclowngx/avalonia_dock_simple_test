using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Models.Plugins;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.ViewModels.Tools;

/// <summary>
/// 展示当前启动会话中所有托管插件的加载和生命周期结果。
/// </summary>
public sealed class PluginStatusViewModel : Tool
{
    internal PluginStatusViewModel(
        PluginModuleCatalog pluginModuleCatalog,
        PluginLifecycleManager lifecycleManager)
    {
        ArgumentNullException.ThrowIfNull(pluginModuleCatalog);
        ArgumentNullException.ThrowIfNull(lifecycleManager);

        Id = DockNameConstant.PluginStatus;
        Title = "插件状态";
        CanClose = true;
        Items = new ObservableCollection<PluginStatusItem>(
            CreateItems(pluginModuleCatalog, lifecycleManager));
    }

    /// <summary>
    /// 供设计器与历史无参激活路径使用。
    /// </summary>
    public PluginStatusViewModel()
        : this(
            ServiceProvider.GetRequiredService<PluginModuleCatalog>(),
            ServiceProvider.GetRequiredService<PluginLifecycleManager>())
    {
    }

    public ObservableCollection<PluginStatusItem> Items { get; }

    private static IReadOnlyList<PluginStatusItem> CreateItems(
        PluginModuleCatalog catalog,
        PluginLifecycleManager manager)
    {
        var items = new List<PluginStatusItem>();
        var moduleIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var module in catalog.Modules
                     .OrderBy(module => module.PluginId, StringComparer.Ordinal))
        {
            moduleIds.Add(module.PluginId);
            items.Add(ToItem(
                module.PluginId,
                module.GetType().Assembly.GetName().Name ?? "未知程序集",
                manager.GetState(module.PluginId)));
        }

        foreach (var state in manager.States
                     .Where(state => !moduleIds.Contains(state.PluginId)))
        {
            items.Add(ToItem(state.PluginId, "未关联托管模块", state));
        }

        return items;
    }

    private static PluginStatusItem ToItem(
        string pluginId,
        string assemblyName,
        PluginLifecycleState? state)
    {
        if (state is null)
        {
            return new PluginStatusItem(
                pluginId,
                assemblyName,
                "已加载 · 无需后台生命周期",
                "—",
                "无",
                "插件模块已完成服务注册，没有需要宿主管理的后台启动或关闭操作。");
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
            detail);
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
}
