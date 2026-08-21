using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>验证 G7 窄 Lease 对独立 Document Scope 的唯一所有权。</summary>
public sealed class DocumentScopeManagerTests
{
    [Fact]
    public void 每个Document拥有独立Scope且Lease释放幂等()
    {
        var services = new ServiceCollection();
        services.AddScoped<TrackedDependency>();
        services.AddScoped<TrackedDocument>();
        services.AddDocumentScopeManagement();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var firstLease = manager.CreatePluginDocument(typeof(TrackedDocument));
        var secondLease = manager.CreatePluginDocument(typeof(TrackedDocument));
        var first = Assert.IsType<TrackedDocument>(firstLease.Model);
        var second = Assert.IsType<TrackedDocument>(secondLease.Model);

        Assert.NotSame(first, second);
        Assert.NotSame(first.Dependency, second.Dependency);
        Assert.Same(first.Dependency, first.SameDependency);

        firstLease.Dispose();
        firstLease.Dispose();
        Assert.True(first.IsDisposed);
        Assert.True(first.Dependency.IsDisposed);
        Assert.False(second.IsDisposed);

        secondLease.Dispose();
        Assert.True(second.IsDisposed);
    }

    [Fact]
    public void Document解析失败会立即释放已经创建的Scoped依赖()
    {
        var services = new ServiceCollection();
        var dependency = new TrackedDependency();
        services.AddScoped(_ => dependency);
        services.AddScoped<FailingDocument>();
        services.AddDocumentScopeManagement();

        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<DocumentScopeManager>();

        Assert.Throws<InvalidOperationException>(() =>
            manager.CreatePluginDocument(typeof(FailingDocument)));
        Assert.True(dependency.IsDisposed);
    }

    [Fact]
    public void 管理器释放时会关闭仍然打开的全部DocumentScope()
    {
        var services = new ServiceCollection();
        services.AddScoped<TrackedDependency>();
        services.AddScoped<TrackedDocument>();
        services.AddDocumentScopeManagement();

        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var first = Assert.IsType<TrackedDocument>(
            manager.CreatePluginDocument(typeof(TrackedDocument)).Model);
        var second = Assert.IsType<TrackedDocument>(
            manager.CreatePluginDocument(typeof(TrackedDocument)).Model);

        manager.Dispose();

        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        Assert.True(first.Dependency.IsDisposed);
        Assert.True(second.Dependency.IsDisposed);
    }

    [Fact]
    public void Lease先取消ClosingToken再释放模型和依赖()
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
        var lease = manager.CreatePluginDocument(typeof(CancellationAwareDocument));
        var document = Assert.IsType<CancellationAwareDocument>(lease.Model);

        Assert.False(lease.ClosingToken.IsCancellationRequested);
        lease.Dispose();

        Assert.True(lease.ClosingToken.IsCancellationRequested);
        Assert.True(document.WasClosingWhenDisposed);
        Assert.Equal(1, document.DisposeCount);
        lease.Dispose();
        Assert.Equal(1, document.DisposeCount);
    }

    public sealed class TrackedDependency : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }

    public sealed class TrackedDocument(
        TrackedDependency dependency,
        TrackedDependency sameDependency) : IPluginDocument, IDisposable
    {
        public TrackedDependency Dependency { get; } = dependency;
        public TrackedDependency SameDependency { get; } = sameDependency;
        public bool IsDisposed { get; private set; }
        public DocumentPresentationState Presentation => new("Scope 测试");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(
            DocumentActivationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public void Dispose() => IsDisposed = true;
    }

    public sealed class FailingDocument : IPluginDocument
    {
        public FailingDocument(TrackedDependency dependency)
        {
            _ = dependency;
            throw new InvalidOperationException("预期的 Document 构造失败");
        }

        public DocumentPresentationState Presentation => new("不可达");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(
            DocumentActivationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    public sealed class CancellationAwareDocument(IDocumentLifetime lifetime) :
        IPluginDocument,
        IDisposable
    {
        public bool WasClosingWhenDisposed { get; private set; }
        public int DisposeCount { get; private set; }
        public DocumentPresentationState Presentation => new("关闭顺序测试");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(
            DocumentActivationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public void Dispose()
        {
            WasClosingWhenDisposed = lifetime.IsClosing;
            DisposeCount++;
        }
    }
}
