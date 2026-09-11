using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Help;
using MyAvaloniaManagement.Business.Presentation;
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

/// <summary>记录一条宿主命令的不可变描述和窄执行实现。</summary>
/// <remarks>
/// Host Handler 由组合根显式创建；记录不保存根 Provider，也不允许按字符串解析服务。
/// 插件命令使用独立的 Registry 事实，不会进入本记录。
/// </remarks>
internal sealed record HostWorkbenchCommandRegistration(
    CommandDescriptor Descriptor,
    IHostWorkbenchCommandHandler Handler);

/// <summary>宿主内建工作台命令的不可变目录。</summary>
/// <remarks>
/// 本类型只负责冻结和查询 Host 事实。执行、可用性和关闭状态属于 Executor，插件声明属于
/// <c>PluginRegistry</c>，从而避免目录演变为服务定位器或第二个运行时。
/// </remarks>
internal sealed class HostWorkbenchCommandCatalog
{
    private readonly IReadOnlyDictionary<CommandId, HostWorkbenchCommandRegistration>
        _registrations;

    internal HostWorkbenchCommandCatalog(
        HostOpenDocumentCommandHandler openDocument,
        HostSaveDocumentCommandHandler saveDocument,
        HostOpenHelpCommandHandler openHelp,
        HostNewDocumentCommandHandler newDocument,
        HostOpenToolCenterCommandHandler? openToolCenter = null,
        HostOpenPluginStatusCommandHandler? openPluginStatus = null)
        : this(
        [
            new HostWorkbenchCommandRegistration(
                new CommandDescriptor(HostWorkbenchCommandIds.NewDocument,
                    "功能中心…", "查找功能，在新标签中开始使用。"),
                newDocument ?? throw new ArgumentNullException(nameof(newDocument))),
            new HostWorkbenchCommandRegistration(
                new CommandDescriptor(
                    HostWorkbenchCommandIds.OpenDocument,
                    "打开…",
                    "从文件中打开一个或多个页面。"),
                openDocument ?? throw new ArgumentNullException(nameof(openDocument))),
            new HostWorkbenchCommandRegistration(
                new CommandDescriptor(
                    HostWorkbenchCommandIds.SaveDocument,
                    "保存",
                    "保存当前活动的可持久化页面。"),
                saveDocument ?? throw new ArgumentNullException(nameof(saveDocument))),
            new HostWorkbenchCommandRegistration(
                new CommandDescriptor(HostWorkbenchCommandIds.OpenHelp,
                    "帮助中心", "阅读产品介绍、架构与理论，探索公式和交互演示。"),
                openHelp ?? throw new ArgumentNullException(nameof(openHelp))),
            .. (openToolCenter is null ? Array.Empty<HostWorkbenchCommandRegistration>() :
                new[] { new HostWorkbenchCommandRegistration(new CommandDescriptor(
                    HostWorkbenchCommandIds.OpenToolCenter, "工具中心…", "搜索、分类和收藏工具，管理工作区工具的显示与隐藏。"), openToolCenter) }),
            .. (openPluginStatus is null ? Array.Empty<HostWorkbenchCommandRegistration>() :
                new[] { new HostWorkbenchCommandRegistration(new CommandDescriptor(
                    HostWorkbenchCommandIds.OpenPluginStatus, "插件状态…", "查看当前会话的插件加载、兼容性、贡献与诊断信息。"), openPluginStatus) }),
        ])
    {
    }

    /// <summary>供单元测试和未来显式 Host 组合使用的冻结入口。</summary>
    internal HostWorkbenchCommandCatalog(
        IEnumerable<HostWorkbenchCommandRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var snapshot = registrations.ToArray();
        if (snapshot.Any(item => item is null || item.Descriptor is null || item.Handler is null))
        {
            throw new ArgumentException("Host Command 注册不得包含 null。", nameof(registrations));
        }

        var duplicate = snapshot
            .GroupBy(item => item.Descriptor.CommandId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Host CommandId 重复：{duplicate.Key.Value}。",
                nameof(registrations));
        }

        _registrations = snapshot.ToDictionary(item => item.Descriptor.CommandId);
    }

    /// <summary>获取按稳定 CommandId 排序的防御性快照。</summary>
    internal IReadOnlyList<HostWorkbenchCommandRegistration> Registrations =>
        _registrations.Values
            .OrderBy(item => item.Descriptor.CommandId.Value, StringComparer.Ordinal)
            .ToArray();

    internal bool TryGet(
        CommandId commandId,
        out HostWorkbenchCommandRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        return _registrations.TryGetValue(commandId, out registration!);
    }
}
