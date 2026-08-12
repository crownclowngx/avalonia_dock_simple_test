using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagement.PluginTests;

public sealed class DocumentScopeManagerTests
{
    [Fact]
    public void 每个Document拥有独立Scope且释放操作幂等()
    {
        var services = new ServiceCollection();
        services.AddScoped<TrackedDependency>();
        services.AddScoped<TrackedDocument>();
        services.AddSingleton<DocumentScopeManager>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        var manager = provider.GetRequiredService<DocumentScopeManager>();

        var first = manager.CreateDocument<TrackedDocument>();
        var second = manager.CreateDocument<TrackedDocument>();

        Assert.NotSame(first, second);
        Assert.NotSame(first.Dependency, second.Dependency);
        Assert.Same(first.Dependency, first.SameDependency);
        Assert.False(first.IsDisposed);
        Assert.False(first.Dependency.IsDisposed);

        Assert.True(manager.Release(first));
        Assert.True(first.IsDisposed);
        Assert.True(first.Dependency.IsDisposed);
        Assert.False(second.IsDisposed);
        Assert.False(manager.Release(first));

        Assert.True(manager.Release(second));
    }

    [Fact]
    public void Document解析失败会立即释放已经创建的Scoped依赖()
    {
        var services = new ServiceCollection();
        var dependency = new TrackedDependency();
        services.AddScoped(_ => dependency);
        services.AddScoped<FailingDocument>();
        services.AddSingleton<DocumentScopeManager>();

        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<DocumentScopeManager>();

        Assert.Throws<InvalidOperationException>(() => manager.CreateDocument<FailingDocument>());
        Assert.True(dependency.IsDisposed);
    }

    [Fact]
    public void 管理器释放时会关闭仍然打开的全部DocumentScope()
    {
        var services = new ServiceCollection();
        services.AddScoped<TrackedDependency>();
        services.AddScoped<TrackedDocument>();
        services.AddSingleton<DocumentScopeManager>();

        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var first = manager.CreateDocument<TrackedDocument>();
        var second = manager.CreateDocument<TrackedDocument>();

        manager.Dispose();

        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        Assert.True(first.Dependency.IsDisposed);
        Assert.True(second.Dependency.IsDisposed);
    }

    [Fact]
    public void ManagementFactory收到Dock关闭通知后释放DocumentScope()
    {
        var services = new ServiceCollection();
        services.AddScoped<TrackedDependency>();
        services.AddScoped<TrackedDocument>();
        services.AddSingleton<DocumentScopeManager>();

        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var extensions = new HostExtensionRegistry([], []);
        var factory = new ManagementFactory(
            extensions,
            manager,
            new MyAvaloniaManagementCommon.Message.MessengerService());
        var document = manager.CreateDocument<TrackedDocument>();

        factory.OnDockableClosed(document);

        Assert.True(document.IsDisposed);
        Assert.True(document.Dependency.IsDisposed);
        Assert.False(manager.Release(document));
    }

    [Fact]
    public void ReleaseCancelsLifetimeBeforeDisposingDocument()
    {
        var services = new ServiceCollection();
        services.AddScoped<CancellationAwareDocument>();
        services.AddDocumentScopeManagement();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var document = manager.CreateDocument<CancellationAwareDocument>();

        Assert.False(document.Lifetime.IsClosing);
        Assert.True(manager.Release(document));

        Assert.True(document.Lifetime.IsClosing);
        Assert.True(document.WasClosingWhenDisposed);
        Assert.Equal(1, document.DisposeCount);
        Assert.False(manager.Release(document));
    }

    public sealed class TrackedDependency : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    public sealed class TrackedDocument(
        TrackedDependency dependency,
        TrackedDependency sameDependency) : Document, IDisposable
    {
        public TrackedDependency Dependency { get; } = dependency;
        public TrackedDependency SameDependency { get; } = sameDependency;
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    public sealed class FailingDocument : Document
    {
        public FailingDocument(TrackedDependency dependency)
        {
            _ = dependency;
            throw new InvalidOperationException("预期的 Document 构造失败");
        }
    }

    public sealed class CancellationAwareDocument(IDocumentLifetime lifetime) : Document, IDisposable
    {
        public IDocumentLifetime Lifetime { get; } = lifetime;
        public bool WasClosingWhenDisposed { get; private set; }
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            WasClosingWhenDisposed = Lifetime.IsClosing;
            DisposeCount++;
        }
    }
}
