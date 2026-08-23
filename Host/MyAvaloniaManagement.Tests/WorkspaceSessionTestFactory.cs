using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.Tests;

/// <summary>为直接对象测试构造具有完整必需依赖的 Workspace Session。</summary>
internal static class WorkspaceSessionTestFactory
{
    internal static WorkspaceSession Create(
        PluginRegistry registry,
        IHostDockableFactory dockableFactory,
        PluginAvailabilityReadModel? availability = null,
        IHostDiagnosticSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(dockableFactory);
        var states = new DocumentPersistenceStateStore();
        var recovery = new DocumentRecoveryRegistry();
        var save = new DocumentSaveService(
            new TestHostStorageService(),
            new DocumentEnvelopeSerializer(),
            new DocumentOperationGate(),
            states,
            recovery,
            TimeProvider.System);
        var close = new DocumentCloseCoordinator(
            save,
            new TestDocumentInteractionService(),
            states);
        var factory = new HostDockFactory();
        var pluginAvailability = availability ?? new PluginAvailabilityReadModel(
            new PluginLifecycleStateStore(registry));
        var catalog = new WorkspaceCatalog(
            new HostWorkspaceCatalog([], []),
            registry,
            pluginAvailability);
        var session = new WorkspaceSession(
            factory,
            catalog,
            dockableFactory,
            states,
            close,
            recovery,
            new DockDocumentLifetime(new DocumentControlRecycling()),
            diagnostics);
        factory.AttachCallbacks(session);
        return session;
    }
}
