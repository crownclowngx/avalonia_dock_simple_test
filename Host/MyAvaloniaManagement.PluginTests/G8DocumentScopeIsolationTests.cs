using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MySmallTools.InitPlug.SecretVideoPlayer;
using MySmallTools.Plugin;
using MySmallTools.ViewModels.SecretVideoPlayer;
using Xunit;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>G8 对四类安全视频 Document 的真实 DI Scope 组合门禁。</summary>
public sealed class G8DocumentScopeIsolationTests
{
    [Fact]
    public void 八个Document拥有独立任务密码扫描会话和播放器()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DocumentScopeManager>();
        services.AddSingleton<IDocumentScopeFactory>(provider =>
            provider.GetRequiredService<DocumentScopeManager>());
        new MySmallToolsPluginModule().ConfigureServices(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        var assembly = typeof(MySmallToolsPluginModule).Assembly;
        var catalog = PluginModuleCatalog.Discover([assembly]);

        var encryptorStrategy = Activate<VideoEncryptorDocumentStrategy>(
            assembly,
            provider,
            catalog);
        var decryptorStrategy = Activate<VideoDecryptorDocumentStrategy>(
            assembly,
            provider,
            catalog);
        var playerStrategy = Activate<SecretVideoDocumentStrategy>(
            assembly,
            provider,
            catalog);
        var libraryStrategy = Activate<SecretVideoLibraryDocumentStrategy>(
            assembly,
            provider,
            catalog);

        var encryptors = Enumerable.Range(0, 2)
            .Select(index => Assert.IsType<VideoEncryptorViewModel>(
                encryptorStrategy.CreateDocument(new DocumentCreationParams(
                    encryptorStrategy.GetMetadata().DocumentTypeId))))
            .ToArray();
        var decryptors = Enumerable.Range(0, 2)
            .Select(index => Assert.IsType<VideoDecryptorViewModel>(
                decryptorStrategy.CreateDocument(new DocumentCreationParams(
                    decryptorStrategy.GetMetadata().DocumentTypeId))))
            .ToArray();
        var players = Enumerable.Range(0, 2)
            .Select(index => Assert.IsType<SecretVideoPlayerViewModel>(
                playerStrategy.CreateDocument(new DocumentCreationParams(
                    playerStrategy.GetMetadata().DocumentTypeId))))
            .ToArray();
        var libraries = Enumerable.Range(0, 2)
            .Select(index => Assert.IsType<SecretVideoLibraryViewModel>(
                libraryStrategy.CreateDocument(new DocumentCreationParams(
                    libraryStrategy.GetMetadata().DocumentTypeId))))
            .ToArray();

        encryptors[0].Password = "enc-a";
        encryptors[1].Password = "enc-b";
        decryptors[0].Password = "dec-a";
        decryptors[1].Password = "dec-b";
        players[0].Password = "player-a";
        players[1].Password = "player-b";
        libraries[0].Password = "library-a";
        libraries[1].Password = "library-b";

        Assert.NotSame(encryptors[0].Queue, encryptors[1].Queue);
        Assert.NotSame(decryptors[0].Queue, decryptors[1].Queue);
        Assert.NotSame(players[0].PlayerViewModel, players[1].PlayerViewModel);
        Assert.NotSame(libraries[0].Browser, libraries[1].Browser);
        Assert.NotSame(libraries[0].PlayerViewModel, libraries[1].PlayerViewModel);
        Assert.Equal(
            8,
            encryptors.Cast<object>()
                .Concat(decryptors)
                .Concat(players)
                .Concat(libraries)
                .Distinct(ReferenceEqualityComparer.Instance)
                .Count());

        var manager = provider.GetRequiredService<DocumentScopeManager>();
        Assert.True(manager.Release(encryptors[0]));
        Assert.Empty(encryptors[0].Password);
        Assert.Equal("enc-b", encryptors[1].Password);
        Assert.True(manager.Release(decryptors[0]));
        Assert.Empty(decryptors[0].Password);
        Assert.Equal("dec-b", decryptors[1].Password);
        Assert.True(manager.Release(players[0]));
        Assert.Empty(players[0].Password);
        Assert.Equal("player-b", players[1].Password);
        Assert.True(manager.Release(libraries[0]));
        Assert.Empty(libraries[0].Password);
        Assert.Equal("library-b", libraries[1].Password);

        foreach (var document in new Dock.Model.Mvvm.Controls.Document[]
                 {
                     encryptors[1],
                     decryptors[1],
                     players[1],
                     libraries[1]
                 })
        {
            Assert.True(manager.Release(document));
        }
    }

    private static TStrategy Activate<TStrategy>(
        System.Reflection.Assembly assembly,
        IServiceProvider provider,
        PluginModuleCatalog catalog)
        where TStrategy : class, IDocumentCreationStrategy =>
        (TStrategy)PluginStrategyActivator.Create<IDocumentCreationStrategy>(
            typeof(TStrategy),
            assembly,
            provider,
            catalog);
}
