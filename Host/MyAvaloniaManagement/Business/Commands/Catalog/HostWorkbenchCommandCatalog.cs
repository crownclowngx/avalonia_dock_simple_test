using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Commands.Catalog;

/// <summary>集中定义宿主内建工作台命令的稳定身份。</summary>
/// <remarks>
/// 这些身份会被后续菜单、快捷键和命令面板共同引用，因此必须与具体 ViewModel 或
/// Avalonia 命令适配器实例分离。G2 只建立无 UI 目录，不创建任何展示对象。
/// </remarks>
internal static class HostWorkbenchCommandIds
{
    internal static readonly CommandId Restart = new("myavalonia.host.command.restart");
    internal static readonly CommandId OpenToolCenter = new("myavalonia.host.command.tool-center.open");
    internal static readonly CommandId OpenPluginStatus = new("myavalonia.host.command.plugin-status.open");
    internal static readonly CommandId NewDocument =
        new("myavalonia.host.command.document.new");
    internal static readonly CommandId OpenDocument =
        new("myavalonia.host.command.document.open");

    internal static readonly CommandId SaveDocument =
        new("myavalonia.host.command.document.save");

    internal static readonly CommandId OpenHelp =
        new("myavalonia.host.command.help.open");
}

/// <summary>宿主内建工作台命令的纯描述目录，不持有 Handler、Workspace 或 Provider。</summary>
/// <remarks>
/// 描述决定稳定身份与展示文字，可在后台冻结和校验。运行期绑定在组合根的 UI 边界单独建立；
/// 查询目录不会为了读取标题而创建工作区。目录不按可用性过滤，动态状态仍由状态查询判断。
/// </remarks>
internal sealed class HostWorkbenchCommandCatalog
{
    private readonly IReadOnlyDictionary<CommandId, CommandDescriptor> _descriptors;

    internal HostWorkbenchCommandCatalog() : this(
    [
        new(HostWorkbenchCommandIds.Restart, "重新启动 Host…", "重启将关闭所有文档，未保存内容会先询问。"),
        new(HostWorkbenchCommandIds.NewDocument, "功能中心…", "查找功能，在新标签中开始使用。"),
        new(HostWorkbenchCommandIds.OpenDocument, "打开…", "从文件中打开一个或多个页面。"),
        new(HostWorkbenchCommandIds.SaveDocument, "保存", "保存当前活动的可持久化页面。"),
        new(HostWorkbenchCommandIds.OpenHelp, "帮助中心", "阅读产品介绍、架构与理论，探索公式和交互演示。"),
        new(HostWorkbenchCommandIds.OpenToolCenter, "工具中心…", "搜索、分类和收藏工具，管理工作区工具的显示与隐藏。"),
        new(HostWorkbenchCommandIds.OpenPluginStatus, "插件看板…", "查看插件状态、版本、兼容矩阵、贡献与诊断信息。"),
    ]) { }

    /// <summary>冻结显式元数据并拒绝重复身份；不接受执行实现或服务解析委托。</summary>
    internal HostWorkbenchCommandCatalog(IEnumerable<CommandDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        var snapshot = descriptors.ToArray();
        if (snapshot.Any(item => item is null))
            throw new ArgumentException("Host Command 描述不得包含 null。", nameof(descriptors));
        var duplicate = snapshot.GroupBy(item => item.CommandId).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Host CommandId 重复：{duplicate.Key.Value}。", nameof(descriptors));
        _descriptors = snapshot.ToDictionary(item => item.CommandId);
    }

    internal IReadOnlyList<CommandDescriptor> Descriptors =>
        _descriptors.Values.OrderBy(item => item.CommandId.Value, StringComparer.Ordinal).ToArray();

    internal bool TryGet(CommandId commandId, out CommandDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        return _descriptors.TryGetValue(commandId, out descriptor!);
    }
}
