using System;
using MyAvaloniaManagement.Business.Presentation.Icons;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>仅在当前运行期使用的页面身份；不进入 SDK、文件信封或布局。</summary>
internal readonly record struct WorkspacePageId(Guid Value);

/// <summary>已发布页面的只读展示快照，不持有插件模型、View、Scope 或 Dock 对象。</summary>
internal sealed record OpenWorkspacePage(WorkspacePageId Id, string Title, string FunctionName,
    string SourceName, int Sequence, bool IsModified, bool CanActivate, HostIconRequest IconRequest)
{
    public string Description => $"{FunctionName} · {SourceName} · 页面 {Sequence}" + (IsModified ? " · 未保存修改" : string.Empty);
}
