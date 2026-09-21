using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Commands.Execution;

/// <summary>组合根显式提供的一条 Host 执行绑定；不保存 Provider 或延迟服务工厂。</summary>
internal sealed record HostWorkbenchCommandBinding(CommandId CommandId, IHostWorkbenchCommandHandler Handler);

/// <summary>冻结 Host 命令身份到实际 Handler 的一对一绑定。</summary>
/// <remarks>
/// 本对象只拥有映射，Handler 的寿命仍由原 DI 容器管理。构造时检查缺失、重复和多余身份，
/// 在工作台展示前失败；状态查询把同一个 Handler 捕获到单次路由，执行器直接使用该实例，
/// 避免 CanExecute 和 Execute 各自查表产生两套分发规则。
/// </remarks>
internal sealed class HostWorkbenchCommandBindings
{
    private readonly IReadOnlyDictionary<CommandId, IHostWorkbenchCommandHandler> _handlers;

    internal HostWorkbenchCommandBindings(HostWorkbenchCommandCatalog catalog,
        IEnumerable<HostWorkbenchCommandBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bindings);
        var snapshot = bindings.ToArray();
        if (snapshot.Any(item => item is null || item.CommandId is null || item.Handler is null))
            throw new ArgumentException("Host 执行绑定不得包含 null。", nameof(bindings));
        var groups = snapshot.GroupBy(item => item.CommandId).ToArray();
        var declared = catalog.Descriptors.Select(item => item.CommandId).ToHashSet();
        var provided = groups.Select(group => group.Key).ToHashSet();
        var failures = groups.Where(group => group.Count() > 1)
            .Select(group => Diagnostic(HostDiagnosticCodes.HostCommandBindingDuplicate, group.Key))
            .Concat(declared.Except(provided).Select(id => Diagnostic(HostDiagnosticCodes.HostCommandBindingMissing, id)))
            .Concat(provided.Except(declared).Select(id => Diagnostic(HostDiagnosticCodes.HostCommandBindingUnknown, id)))
            .ToArray();
        if (failures.Length != 0) throw new HostCompositionException(failures);
        _handlers = snapshot.ToDictionary(item => item.CommandId, item => item.Handler);
    }

    internal IHostWorkbenchCommandHandler GetRequired(CommandId commandId) => _handlers[commandId];

    private static HostCompositionDiagnostic Diagnostic(string code, CommandId id) => new(code, id.Value,
        [new HostCompositionContributor(nameof(HostWorkbenchCommandBindings), typeof(HostWorkbenchCommandBindings).Assembly.GetName().Name!)]);
}
