using System;
using System.Linq;
using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.Business.ToolCenter;

/// <summary>中心与命令面板共用成功访问记录；焦点由桌面窗口适配器处理。</summary>
internal sealed class ToolCenterActions(WorkspaceSession workspace, ToolCenterPreferences preferences)
{
    internal event EventHandler? FocusRequested;
    internal ToolOperationResult Open(string id, bool focus = false)
    {
        var result = workspace.OpenTool(id);
        if (!result.Succeeded) return result;
        var descriptor = workspace.GetRegisteredTools().First(item => item.Descriptor.ToolTypeId.Value == id).Descriptor;
        preferences.RememberAccess(id, descriptor.DisplayName);
        if (focus) FocusRequested?.Invoke(this, EventArgs.Empty);
        return result;
    }
    internal ToolOperationResult Hide(string id) => workspace.SetToolVisibility(id, false);
    internal ToolBatchResult HideAll() => workspace.HideAllTools();
}
