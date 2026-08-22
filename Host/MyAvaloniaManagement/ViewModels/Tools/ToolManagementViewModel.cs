using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Models.Tools;

namespace MyAvaloniaManagement.ViewModels.Tools;

/// <summary>
/// 展示 Tool 只读状态，并把显隐意图交给唯一拥有工作区状态的 Session。
/// </summary>
/// <remarks>
/// ViewModel 不读取 Root Dock、Dock Tool、Factory 字典或服务容器。Session 完成一次布局提交后，
/// 本对象重新捕获纯数据 ReadModel；释放时解除订阅，避免单例 Session 延长已结束绑定对象的生命周期。
/// </remarks>
internal sealed partial class ToolManagementViewModel : ObservableObject, IDisposable
{
    private readonly ToolWorkspaceReadModel _readModel;
    private readonly WorkspaceSession _workspace;
    private bool _disposed;

    /// <summary>获取或设置可由用户管理的工具项集合。</summary>
    [ObservableProperty]
    private ObservableCollection<ToolManagementItem> _toolItems = new();

    /// <summary>使用无 Dock 输出的 ReadModel 和工作区命令入口创建 Tool 管理器。</summary>
    public ToolManagementViewModel(
        ToolWorkspaceReadModel readModel,
        WorkspaceSession workspace)
    {
        _readModel = readModel ?? throw new ArgumentNullException(nameof(readModel));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _workspace.LayoutChanged += OnWorkspaceLayoutChanged;
        LoadTools();
    }

    /// <summary>从纯数据投影加载全部可管理 Tool。</summary>
    private void LoadTools()
    {
        var states = _readModel.Capture();
        var activeIds = states.Select(state => state.ToolId).ToHashSet(StringComparer.Ordinal);
        for (var index = ToolItems.Count - 1; index >= 0; index--)
        {
            if (!activeIds.Contains(ToolItems[index].ToolId))
            {
                ToolItems.RemoveAt(index);
            }
        }

        foreach (var state in states)
        {
            var item = ToolItems.FirstOrDefault(candidate => candidate.ToolId == state.ToolId);
            if (item is null)
            {
                item = new ToolManagementItem { ToolId = state.ToolId };
                ToolItems.Add(item);
            }
            item.DisplayName = state.DisplayName;
            item.IsVisible = state.IsVisible;
            item.CanClose = state.CanHide;
        }
    }

    /// <summary>根据当前投影请求隐藏或恢复指定 Tool。</summary>
    [RelayCommand]
    public void ToggleToolVisibility(ToolManagementItem item)
    {
        if (item is null || !item.CanClose)
        {
            return;
        }
        _workspace.TrySetToolVisibility(item.ToolId, !item.IsVisible);
    }

    /// <summary>解除对 Session 的定向订阅；重复调用安全。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _workspace.LayoutChanged -= OnWorkspaceLayoutChanged;
    }

    private void OnWorkspaceLayoutChanged(object? sender, EventArgs args) => LoadTools();
}
