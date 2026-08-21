using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 仅供尚待 G9-G12 迁移的真实业务插件测试使用的旧契约夹具。
/// </summary>
/// <remarks>
/// G7 已从生产 Host 删除 <see cref="IDocumentScopeFactory"/>。三个业务插件套件仍需验证现有代码，
/// 因此测试程序集在自己的组合根中临时实现旧端口；它不引用 Host 的 Document V2 Scope Manager，
/// 也不能进入生产依赖注入。每个旧 Document 仍获得独立 Scope，以避免测试降低原有隔离断言。
/// </remarks>
internal sealed class LegacyPluginDocumentScopeFactory(
    IServiceScopeFactory scopeFactory) : IDocumentScopeFactory, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Document, LegacyLease> _scopes =
        new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public TDocument CreateDocument<TDocument>() where TDocument : Document
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        var scope = scopeFactory.CreateScope();
        try
        {
            var lifetime = scope.ServiceProvider.GetRequiredService<LegacyPluginDocumentLifetime>();
            var document = scope.ServiceProvider.GetRequiredService<TDocument>();
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _scopes.Add(document, new LegacyLease(scope, lifetime));
            }

            return document;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    /// <summary>释放指定旧文档的测试 Scope；重复释放返回 <see langword="false"/>。</summary>
    internal bool Release(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        LegacyLease? lease;
        lock (_gate)
        {
            if (!_scopes.Remove(document, out lease))
            {
                return false;
            }
        }

        lease.Dispose();
        return true;
    }

    public void Dispose()
    {
        LegacyLease[] scopes;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            scopes = [.. _scopes.Values];
            _scopes.Clear();
        }

        foreach (var scope in scopes)
        {
            scope.Dispose();
        }
    }

    private sealed record LegacyLease(
        IServiceScope Scope,
        LegacyPluginDocumentLifetime Lifetime) : IDisposable
    {
        public void Dispose()
        {
            Lifetime.Cancel();
            Scope.Dispose();
        }
    }
}

/// <summary>为旧业务插件测试提供每 Scope 一个的只读关闭令牌。</summary>
internal sealed class LegacyPluginDocumentLifetime : IDocumentLifetime, IDisposable
{
    private readonly CancellationTokenSource _closing = new();
    public CancellationToken ClosingToken => _closing.Token;
    public bool IsClosing => _closing.IsCancellationRequested;
    internal void Cancel() => _closing.Cancel();
    public void Dispose() => _closing.Dispose();
}

/// <summary>集中约束旧业务插件测试端口的注册，避免复制生产 G6 组合代码。</summary>
internal static class LegacyPluginDocumentScopeFactoryExtensions
{
    internal static IServiceCollection AddLegacyPluginDocumentScopesForTests(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<LegacyPluginDocumentLifetime>();
        services.TryAddScoped<IDocumentLifetime>(provider =>
            provider.GetRequiredService<LegacyPluginDocumentLifetime>());
        services.TryAddSingleton<LegacyPluginDocumentScopeFactory>();
        services.TryAddSingleton<IDocumentScopeFactory>(provider =>
            provider.GetRequiredService<LegacyPluginDocumentScopeFactory>());
        return services;
    }
}
