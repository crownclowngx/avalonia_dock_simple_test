using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>为纯布局 PluginTests 建立不访问文件系统的最小 Workspace Session。</summary>
internal static class PluginTestWorkspaceSession
{
    internal static WorkspaceSession Create(
        PluginRegistry registry,
        DocumentScopeManager scopeManager,
        PluginAvailabilityReadModel? availability = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(scopeManager);
        _ = scopeManager;
        var states = new DocumentPersistenceStateStore();
        var recovery = new DocumentRecoveryRegistry();
        var save = new DocumentSaveService(
            new NullStorage(),
            new DocumentEnvelopeSerializer(),
            new DocumentOperationGate(),
            states,
            recovery,
            TimeProvider.System);
        var close = new DocumentCloseCoordinator(
            save,
            new NullInteraction(),
            states,
            new WorkbenchDocumentCommandLeaseStore());
        var dockFactory = new HostDockFactory();
        var pluginAvailability = availability ?? new PluginAvailabilityReadModel(
            new PluginLifecycleStateStore(registry));
        var catalog = new WorkspaceCatalog(
            new HostWorkspaceCatalog([], []),
            registry,
            pluginAvailability);
        var session = new WorkspaceSession(
            dockFactory,
            catalog,
            new LayoutOnlyDockableFactory(),
            states,
            close,
            recovery,
            new DockDocumentLifetime(new DocumentControlRecycling()));
        dockFactory.AttachCallbacks(session);
        return session;
    }

    private sealed class LayoutOnlyDockableFactory : IHostDockableFactory
    {
        public Document CreateHostDocument(
            DocumentTypeId documentTypeId,
            NewDocumentActivation activation) => new() { Title = activation.Title };

        public ValueTask<Document> CreateDocumentAsync(
            DocumentTypeId documentTypeId,
            DocumentActivation context)
        {
            ArgumentNullException.ThrowIfNull(documentTypeId);
            ArgumentNullException.ThrowIfNull(context);
            return ValueTask.FromResult<Document>(new Document { Title = context.Title });
        }

        public Tool CreateTool(ToolTypeId toolTypeId) =>
            throw new NotSupportedException("布局测试 seam 不负责创建插件 Tool。");
    }

    private sealed class NullStorage : IHostStorageService
    {
        public Task<IReadOnlyList<string>> PickOpenFilesAsync() =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickSaveFileAsync(string documentDisplayName) =>
            Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
        public bool FileExists(string path) => false;
        public bool DirectoryExists(string path) => false;
        public long GetFileLength(string path) => 0;
        public Task<string> ReadAllTextAsync(string path) => Task.FromResult(string.Empty);
        public Task WriteAllTextAsync(string path, string content) => Task.CompletedTask;
    }

    private sealed class NullInteraction : IDocumentInteractionService
    {
        public Task<DocumentCloseChoice> ConfirmCloseAsync(
            IReadOnlyList<string> documentNames,
            bool isApplicationExit) => Task.FromResult(DocumentCloseChoice.Cancel);
        public Task<bool> ConfirmRecoveryAsync(string fileName) => Task.FromResult(false);
        public Task ShowErrorAsync(string message) => Task.CompletedTask;
    }
}
