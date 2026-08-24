using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Dock.Model.Core;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Business.Plugins.Registration;

namespace MyAvaloniaManagement;

/// <summary>
/// 为 Host internal Dock Adapter 创建并返回已经预构建的根级 DataTemplate View。
/// </summary>
/// <remarks>
/// Locator 不扫描程序集、不读取插件目录，也不根据命名猜测类型。普通插件模型不能直接匹配
/// Dock DataTemplate；只有携带已冻结注册事实的 Adapter 可以请求 View，从结构上保证插件模型
/// 永远不会伪装成 Dock 对象。
/// </remarks>
internal sealed class ViewLocator(
    WorkspaceCatalog catalog,
    IHostDiagnosticSink? diagnostics = null) : IDataTemplate
{
    private readonly WorkspaceCatalog _catalog = catalog ??
        throw new ArgumentNullException(nameof(catalog));

    /// <summary>在 Adapter 发布前精确构造一次 View，并把普通模型设置为 DataContext。</summary>
    /// <remarks>
    /// 预构建使 View 构造失败发生在 Dock 集合变化以前。Document 调用方可以释放待提交 Scope，
    /// Tool 调用方也可以只隔离失败 Tool，而不会在用户点击标签后才暴露半发布状态。
    /// </remarks>
    internal Control Prepare(IManagedDockableViewHost adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (adapter.PreparedView is { } existing)
        {
            return existing;
        }

        var registration = adapter.ViewRegistration;
        if (!_catalog.TryGetView(adapter.Model.GetType(), out var registered) ||
            registered.GetType() != registration.GetType() ||
            registered.ViewType != registration.ViewType)
        {
            throw new InvalidOperationException(
                $"模型 {adapter.Model.GetType().FullName} 的 View 注册与 Adapter 不一致。");
        }

        try
        {
            var view = registration.Factory();
            view.DataContext = adapter.Model;
            adapter.AttachPreparedView(view);
            return view;
        }
        catch (Exception exception)
        {
            ReportViewFailure(registration, exception);
            throw;
        }
    }

    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
        }

        if (data is IManagedDockableViewHost)
        {
            // Dock 标签的图标、标题、修改标记和关闭按钮 Presenter 都以 Dockable
            // 作为 Content。它们的专用模板在标签脱离旧 Dock 的短暂窗口内可能失效，
            // 此时会回退到应用级 DataTemplate。这里绝不能发布 Adapter 唯一拥有的
            // 正文 View，否则多个辅助 Presenter 会争用同一个视觉父级。
            // 真实正文 View 只允许由 DocumentControlRecycling 取得。
            return new Border
            {
                IsVisible = false,
                IsHitTestVisible = false
            };
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
        (data is IManagedDockableViewHost || data is IDockable);

    private void ReportViewFailure(
        IWorkspaceViewRegistration registration,
        Exception exception)
    {
        // 诊断草稿可以携带异常供白名单层提取类型，但持久化记录不会保存异常正文。
        diagnostics?.Report(new HostDiagnosticDraft(
            "VIEW_CREATION_FAILED",
            HostDiagnosticPhase.ExtensionDiscovery)
        {
            PluginId = registration is PluginWorkspaceViewRegistration plugin
                ? plugin.OwnerId
                : null,
            AssemblyName = registration.ViewType.Assembly.GetName(),
            StableId = registration.ViewType.FullName,
            Exception = exception,
        });
    }
}
