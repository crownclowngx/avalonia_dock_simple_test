using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.Business.Workspace;

internal sealed partial class WorkspaceSession
{
    // 页面对象仍由 _ownedDocuments 唯一拥有；此表只给已发布页面保留稳定展示序号。
    // 过滤与排序不重新编号，关闭时删除引用，避免搜索成为第二个生命周期所有者。
    private readonly Dictionary<ManagedDocumentDockable, int> _publishedPages = new(ReferenceEqualityComparer.Instance);
    private int _nextPageSequence;

    /// <summary>已发布页面的标题、修改状态或成员发生变化；观察者只需重新读取快照。</summary>
    internal event EventHandler? PagesChanged;

    internal IReadOnlyList<OpenWorkspacePage> GetOpenPages() => _publishedPages
        .Where(pair => IsPublishedPage(pair.Key)).OrderBy(pair => pair.Value)
        .Select(pair =>
        {
            var page = pair.Key;
            var descriptor = page.Registration.Descriptor;
            var owner = page.PluginRegistration?.OwnerId;
            return new OpenWorkspacePage(page.PageId, page.Title ?? descriptor.DisplayName, descriptor.DisplayName,
                owner?.Value ?? "内置", pair.Value, page.IsModified, CanActivatePage(page), new(owner, descriptor.IconPath));
        }).ToArray();

    /// <summary>执行时重查页面身份和关闭状态，绝不按标题替换目标或回退成新建。</summary>
    internal bool TryActivatePage(WorkspacePageId id)
    {
        var page = _publishedPages.Keys.FirstOrDefault(candidate => candidate.PageId == id);
        if (page is null || !CanActivatePage(page)) return false;
        DockFactory.SetActiveDockable(page);
        DockFactory.SetFocusedDockable(_rootDock!, page);
        PublishActiveDocumentIfChanged(page);
        return true;
    }

    private bool IsPublishedPage(ManagedDocumentDockable page) => !_disposed && _rootDock is not null &&
        _ownedDocuments.Contains(page) && DockTreeNavigator.FindDocumentDock(_rootDock, page) is not null;

    private bool CanActivatePage(ManagedDocumentDockable page) => _acceptingCreations && IsPublishedPage(page) &&
        !_documentCloseCoordinator.IsClosing(page) &&
        (page.PluginRegistration is null || _catalog.TryGetAvailablePluginDocument(page.Registration.Descriptor.DocumentTypeId, out _));

    private void TrackPublishedPage(ManagedDocumentDockable page)
    {
        if (_publishedPages.ContainsKey(page)) return;
        _publishedPages.Add(page, ++_nextPageSequence);
        page.PropertyChanged += OnPagePresentationChanged;
        NotifyPagesChanged();
    }

    private void UntrackPublishedPage(ManagedDocumentDockable page)
    {
        if (!_publishedPages.Remove(page)) return;
        page.PropertyChanged -= OnPagePresentationChanged;
        NotifyPagesChanged();
    }

    private void OnCloseStateChanged(object? sender, EventArgs args) => NotifyPagesChanged();

    private void OnPagePresentationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ManagedDocumentDockable.Title) or nameof(ManagedDocumentDockable.IsModified))
            NotifyPagesChanged();
    }

    private void NotifyPagesChanged()
    {
        // 展示观察者不能中断页面发布／回收；异常继续进入既有脱敏诊断通道。
        foreach (EventHandler handler in PagesChanged?.GetInvocationList() ?? [])
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception exception)
            {
                try { _diagnostics?.Report(new HostDiagnosticDraft(HostDiagnosticCodes.WorkbenchCommandStateObserverFailed,
                    HostDiagnosticPhase.WorkbenchCommand) { Exception = exception }); }
                catch { /* 诊断失败不改变已经提交的页面所有权。 */ }
            }
        }
    }
}
