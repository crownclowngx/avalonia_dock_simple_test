using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Dock.Model.Core;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Helpers;

namespace MyAvaloniaManagement;

/// <summary>
/// 只根据已冻结 Plugin Registry 创建根级 DataTemplate View。
/// </summary>
/// <remarks>
/// Locator 不扫描程序集、不读取插件目录，也不根据命名猜测类型。新增 View 必须在宿主组合根或
/// 插件注册上下文中显式声明，这保证同一类型在一次 Runtime 中只有一个可审阅映射。
/// </remarks>
internal sealed class ViewLocator(
    PluginRegistry registry,
    IHostDiagnosticSink? diagnostics = null) : IDataTemplate
{
    private readonly PluginRegistry _registry = registry ??
        throw new ArgumentNullException(nameof(registry));

    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
        }

        if (_registry.TryGetView(data.GetType(), out var registration))
        {
            try
            {
                return registration.Factory();
            }
            catch (Exception exception)
            {
                // View 构造属于显示期失败，不能破坏已经发布的 Registry。G15 尚未完成全局异常
                // 脱敏，因此本边界绝不把异常对象交给持久化会话，只保留异常类型这一白名单事实。
                diagnostics?.Report(new HostDiagnosticDraft(
                    "VIEW_CREATION_FAILED",
                    HostDiagnosticPhase.ExtensionDiscovery)
                {
                    PluginId = registration.OwnerId,
                    AssemblyName = registration.ViewType.Assembly.GetName(),
                    StableId = registration.ViewType.FullName,
                    Exception = exception,
                });
                return new TextBlock { Text = $"无法显示 {data.GetType().Name}" };
            }
        }

        if (data is IDockable dockable)
        {
            return new TextBlock { Text = $"未登记 {dockable.Title} 的视图" };
        }

        throw new InvalidOperationException(
            $"没有为类型 {data.GetType().FullName} 登记 View，且该类型不属于 Dockable。");
    }

    public bool Match(object? data) =>
        data is not null &&
        (_registry.TryGetView(data.GetType(), out _) || data is IDockable);
}
