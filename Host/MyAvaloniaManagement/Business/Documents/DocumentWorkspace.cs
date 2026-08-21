using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.ViewModels;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>
/// 将通用 Dock 对象图适配为面向文档的添加、激活、查重和当前项查询。
/// 该适配层隔离 Dock 实现细节，使持久化流程不依赖具体的树遍历方式。
/// </summary>
internal sealed class DocumentWorkspace(
    ManagementFactory factory,
    DocumentPersistenceStateStore persistenceStates,
    DocumentRecoveryRegistry recoveryRegistry)
{
    internal IDockable? GetActiveDocument() => GetDocumentDock()?.ActiveDockable;

    internal bool TryActivate(IRootDock? root, string filePath)
    {
        if (root is null)
        {
            return false;
        }

        foreach (var dockable in DockTreeNavigator.Enumerate(root))
        {
            if (dockable is not ManagedDocumentDockable document ||
                !persistenceStates.TryGet(document, out var state) ||
                !DocumentPathIdentity.Equals(state.FilePath, filePath))
            {
                continue;
            }

            var documentDock = DockTreeNavigator.FindDocumentDock(root, dockable);
            if (documentDock is null)
            {
                continue;
            }

            documentDock.ActiveDockable = dockable;
            return true;
        }

        if (recoveryRegistry.TryGetBySourcePath(filePath, out var recovered))
        {
            var recoveredDock = DockTreeNavigator.FindDocumentDock(root, recovered);
            if (recoveredDock is not null)
            {
                recoveredDock.ActiveDockable = recovered;
                return true;
            }
        }

        return false;
    }

    internal static IReadOnlyList<ManagedDocumentDockable> GetDocuments(IRootDock? root) =>
        root is null
            ? []
            : DockTreeNavigator.Enumerate(root).OfType<ManagedDocumentDockable>().ToArray();

    private DocumentDock? GetDocumentDock() =>
        factory.GetDockable<IDocumentDock>("Files") as DocumentDock;
}
