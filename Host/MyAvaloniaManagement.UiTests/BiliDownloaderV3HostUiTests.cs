using Avalonia.Headless.XUnit;
using BiliDownloader.Constants;
using BiliDownloader.Plugin;
using BiliDownloader.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// 使用真实 Host Workspace、Dock Adapter 与保存服务验收 BiliDownloader V3。
/// 业务下载、SQLite 与 FFmpeg 已由插件单元测试覆盖；这里仅验证 Host 拥有的组合和提交时序。
/// </summary>
public sealed class BiliDownloaderV3HostUiTests
{
    [AvaloniaFact]
    public async Task Host保存捕获后修改保留Dirty且第二次保存正确清脏()
    {
        using var composition = BiliHostComposition.Create();
        var document = await composition.Workspace.CreateAndPublishDocumentAsync(
            BiliDownloaderContributionIds.DownloadDocument,
            new NewDocumentActivation(
                "Bili 保存竞争",
                BiliDownloaderContributionIds.QuickUrlIntent));
        var model = Assert.IsType<BiliDownloaderViewModel>(document.Model);
        model.VideoParse.Url = "BV1-captured";
        composition.Storage.SavePath = Path.Combine(composition.DirectoryPath, "bili.mydoc");

        var saveService = composition.Provider.GetRequiredService<DocumentSaveService>();
        var firstSave = saveService.SaveAsync(document);
        await composition.Storage.PrimaryWriteStarted.WaitAsync(TimeSpan.FromSeconds(5));

        // Host 已捕获旧 Revision，但主文件提交尚未完成。此时修改命名模板会推进业务
        // Revision；AcceptChanges 只能确认已经落盘的旧版本，绝不能清除更新后的 Dirty。
        model.NamingTemplate.Template = "{title}-edited-during-save";
        composition.Storage.ReleasePrimaryWrite();
        var firstResult = await firstSave;

        Assert.Equal(DocumentSaveStatus.Saved, firstResult.Status);
        Assert.True(firstResult.HasPendingChanges);
        Assert.True(model.IsDirty);
        Assert.True(document.IsModified);

        // 第二次保存捕获当前 Revision。提交完成后 Model 与 Adapter 的修改标记必须同时清除，
        // 证明清脏责任仍由插件 Revision 契约与 Host 提交点协作，而不是由测试直接改状态。
        var secondResult = await saveService.SaveAsync(document);
        Assert.Equal(DocumentSaveStatus.Saved, secondResult.Status);
        Assert.False(secondResult.HasPendingChanges);
        Assert.False(model.IsDirty);
        Assert.False(document.IsModified);
    }

    private sealed class BiliHostComposition : IDisposable
    {
        private readonly string _directory;
        private readonly HostDiagnosticSession _diagnostics;
        private readonly PluginProviderOwner _pluginProviders;
        private readonly DocumentScopeRegistry _documentScopes;
        private bool _disposed;

        private BiliHostComposition(
            string directory,
            HostDiagnosticSession diagnostics,
            ServiceProvider provider,
            PluginProviderOwner pluginProviders,
            DocumentScopeRegistry documentScopes,
            WorkspaceSession workspace,
            ControlledHostStorageService storage)
        {
            _directory = directory;
            _diagnostics = diagnostics;
            Provider = provider;
            _pluginProviders = pluginProviders;
            _documentScopes = documentScopes;
            Workspace = workspace;
            Storage = storage;
        }

        internal ServiceProvider Provider { get; }
        internal WorkspaceSession Workspace { get; }
        internal ControlledHostStorageService Storage { get; }
        internal string DirectoryPath => _directory;

        internal static BiliHostComposition Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"bili-g12-ui-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var diagnostics = HostDiagnosticSession.Start(directory);
            var registryBuilder = new PluginRegistryBuilder();
            var pluginProviders = new PluginProviderOwner();
            var documentScopes = new DocumentScopeRegistry();
            var storage = new ControlledHostStorageService();
            var services = new ServiceCollection();
            services.AddApplicationServices(registryBuilder, pluginProviders, documentScopes);
            services.AddViewModels();
            services.AddSingleton<IHostStorageService>(storage);
            services.AddSingleton(diagnostics);
            services.AddSingleton<IHostDiagnosticSink>(diagnostics);
            services.AddSingleton(PluginModuleCatalog.CreateForTests(
            [
                (BiliDownloaderContributionIds.Plugin,
                    (IPluginModule)new BiliDownloaderPluginModule()),
            ]));
            var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            });
            pluginProviders.Compose(
                provider.GetRequiredService<PluginModuleCatalog>(),
                provider,
                registryBuilder,
                documentScopes,
                diagnostics);
            _ = provider.GetRequiredService<PluginRegistry>();
            // 本测试只验收 Host 保存提交竞争，不启动会访问 SQLite、设置和 FFmpeg 的插件
            // Lifecycle。Lifecycle 的成功、失败、取消、停止与新对象图恢复由插件专项测试独立覆盖；
            // 此处仅设置 Host 自己的可用性投影，使 Workspace 能进入目标 Document 链路。
            provider.GetRequiredService<PluginLifecycleStateStore>().SetState(
                new PluginLifecycleState(
                    BiliDownloaderContributionIds.Plugin,
                    PluginLifecycleStatus.Ready));
            var workspace = provider.GetRequiredService<WorkspaceSession>();
            var layout = workspace.CreateLayout();
            workspace.DockFactory.InitLayout(layout);
            return new BiliHostComposition(
                directory,
                diagnostics,
                provider,
                pluginProviders,
                documentScopes,
                workspace,
                storage);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Workspace.Dispose();
            _documentScopes.CloseAll();
            _pluginProviders.Dispose();
            Provider.Dispose();
            _diagnostics.Dispose();
            // BiliDownloader 的 provider 可能构造 SQLite 工厂；先释放全部对象图再清连接池，
            // 避免测试目录删除与池中句柄竞争。
            SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
