using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Commands.Execution;
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
    internal static readonly CommandId OpenDocument =
        new("myavalonia.host.command.document.open");

    internal static readonly CommandId SaveDocument =
        new("myavalonia.host.command.document.save");
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
        HostSaveDocumentCommandHandler saveDocument)
        : this(
        [
            new HostWorkbenchCommandRegistration(
                new CommandDescriptor(
                    HostWorkbenchCommandIds.OpenDocument,
                    "打开…",
                    "从文件中打开一个或多个 Document。"),
                openDocument ?? throw new ArgumentNullException(nameof(openDocument))),
            new HostWorkbenchCommandRegistration(
                new CommandDescriptor(
                    HostWorkbenchCommandIds.SaveDocument,
                    "保存",
                    "保存当前活动的可持久化 Document。"),
                saveDocument ?? throw new ArgumentNullException(nameof(saveDocument))),
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
