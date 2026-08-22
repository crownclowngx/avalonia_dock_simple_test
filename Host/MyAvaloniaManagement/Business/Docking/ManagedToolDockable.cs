using System;
using System.Threading;
using Avalonia.Controls;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Docking;

/// <summary>把插件 Provider 拥有的普通 Tool 模型投影为 Dock Tool。</summary>
/// <remarks>
/// Adapter 只拥有 View 和 Dock 展示状态；模型仍是插件级 singleton，由所属 Provider 在插件退出时释放。
/// 因此隐藏或恢复 Tool 不会重建业务状态，也不会让 Dock 获得模型释放权。
/// </remarks>
internal sealed class ManagedToolDockable : Tool, IManagedDockableViewHost, IDisposable
{
    private readonly ActivatedWorkspaceTool _activation;
    private readonly ManagedDockableViewLease _view = new();
    private int _disposed;

    internal ManagedToolDockable(ActivatedWorkspaceTool activation)
    {
        _activation = activation ?? throw new ArgumentNullException(nameof(activation));
        var descriptor = activation.Registration.Descriptor;
        Id = descriptor.ToolTypeId.Value;
        Title = descriptor.DisplayName;
        Context = activation.Model;
        CanClose = descriptor.CloseBehavior == ToolCloseBehavior.Hide;
        CanPin = true;
        CanFloat = false;
    }

    internal IWorkspaceToolRegistration Registration => _activation.Registration;
    public object Model => _activation.Model;
    public IWorkspaceViewRegistration ViewRegistration =>
        Registration is PluginToolRegistration plugin
            ? new PluginWorkspaceViewRegistration(
                plugin.OwnerId,
                plugin.ModelType,
                plugin.ViewType,
                plugin.ViewFactory)
            : new HostWorkspaceViewRegistration(
                Registration.ModelType,
                Registration.ViewType,
                Registration.ViewFactory);
    public Control? PreparedView => _view.View;
    public void AttachPreparedView(Control view) => _view.Attach(view);
    public void ReleasePreparedView() => _view.Release();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _view.Release();
        }
    }
}
