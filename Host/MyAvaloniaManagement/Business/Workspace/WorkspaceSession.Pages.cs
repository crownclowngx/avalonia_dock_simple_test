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

    /// <summary>在本次同步查询内捕获成员关系，按稳定发布序号返回页面展示记录。</summary>
    internal IReadOnlyList<OpenWorkspacePage> GetOpenPages()
    {
        if (_disposed || _rootDock is null || _publishedPages.Count == 0) return [];
        var layout = WorkspaceLayoutQuerySnapshot.Capture(_rootDock);
        // 已发布、仍由 Session 拥有且实际挂接三个条件都必须成立。成员关系查一次即可，
        // 不为 CanActivate 再遍历整树；关闭中和插件可用性仍从原权威来源即时读取。
        return _publishedPages
            .Where(pair => _ownedDocuments.Contains(pair.Key) && layout.FindDocumentDock(pair.Key) is not null)
            .OrderBy(pair => pair.Value)
            .Select(pair =>
            {
                var page = pair.Key;
                var descriptor = page.Registration.Descriptor;
                var owner = page.PluginRegistration?.OwnerId;
                return new OpenWorkspacePage(page.PageId, page.Title ?? descriptor.DisplayName, descriptor.DisplayName,
                    owner?.Value ?? "内置", pair.Value, page.IsModified, CanActivatePublishedPage(page), new(owner, descriptor.IconPath));
            }).ToArray();
    }

    /// <summary>执行时重查页面身份和关闭状态，绝不按标题替换目标或回退成新建。</summary>
    internal bool TryActivatePage(WorkspacePageId id)
    {
        var page = _publishedPages.Keys.FirstOrDefault(candidate => candidate.PageId == id);
        if (page is null || !CanActivatePage(page)) return false;
        ActivateDockable(page);
        PublishActiveDocumentIfChanged(page);
        return true;
    }

    /// <summary>
    /// 为按钮和焦点恢复只检查指定页面，不排序、格式化或投影其他页面。沿用已发布引用表和
    /// 实时挂接/关闭/可用性规则，不建立第二份 PageId 索引或需要失效通知的长期缓存。
    /// </summary>
    internal bool CanActivatePage(WorkspacePageId id)
    {
        var page = _publishedPages.Keys.FirstOrDefault(candidate => candidate.PageId == id);
        return page is not null && CanActivatePage(page);
    }

    private bool IsPublishedPage(ManagedDocumentDockable page) => !_disposed && _rootDock is not null &&
        _ownedDocuments.Contains(page) && DockTreeNavigator.FindDocumentDock(_rootDock, page) is not null;

    /// <summary>活动目标和焦点属于实际承载窗口；最小化浮窗先还原，再激活同一实例。</summary>
    internal void ActivateDockable(Dock.Model.Core.IDockable target)
    {
        if (_rootDock is null) return;
        var window = DockTreeNavigator.FindWindow(_rootDock, target);
        if (window?.Host is { } host)
        {
            if (host.GetWindowState() == Dock.Model.Core.DockWindowState.Minimized)
                host.SetWindowState(Dock.Model.Core.DockWindowState.Normal);
            host.SetActive();
        }
        else if (window is null && DockFactory.WindowContext.MainWindow is { } main)
        {
            if (main.WindowState == Avalonia.Controls.WindowState.Minimized)
                main.WindowState = Avalonia.Controls.WindowState.Normal;
            main.Activate();
        }
        DockFactory.SetActiveDockable(target);
        DockFactory.SetFocusedDockable(window?.Layout ?? DockFactory.FindRoot(target, _ => true) ?? _rootDock, target);
    }

    // 执行入口仍检查实时树，不复用此前展示查询的关系快照。
    private bool CanActivatePage(ManagedDocumentDockable page) => _acceptingCreations && IsPublishedPage(page) &&
        CanActivatePublishedPage(page);

    /// <summary>调用方已确认发布和挂接后，再读取当前退出、关闭与插件可用性。</summary>
    private bool CanActivatePublishedPage(ManagedDocumentDockable page) => _acceptingCreations &&
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
