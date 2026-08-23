using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Documents.Ownership;

/// <summary>
/// 统一管理由依赖注入容器创建的 Document 作用域。
/// </summary>
/// <remarks>
/// Document 的可见生命周期由 Dock 决定，而服务的生命周期由 Microsoft DI 决定。
/// 本类是两者之间唯一的所有权桥梁：创建时保存 Document 与 Scope 的一一对应关系，
/// Dock 真正确认关闭后再释放 Scope。插件不能自行保存或释放 Scope，避免提前释放、重复释放，
/// 以及从根容器解析可释放 transient 后一直存活到进程退出的问题。
/// </remarks>
internal sealed class DocumentScopeManager : IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly object _syncRoot = new();
    private readonly Dictionary<object, DocumentScopeLease> _scopes =
        new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public DocumentScopeManager(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    /// <inheritdoc />
    /// <summary>
    /// 按声明式 Registry 已验证的精确模型类型创建并托管普通插件 Document Scope Lease。
    /// </summary>
    /// <remarks>
    /// 本入口不扫描类型、不接受任意服务名，也不要求模型继承 Dock。返回模型随后由 Host internal
    /// Adapter 承载；插件始终只观察自己的业务对象和 ClosingToken。
    /// </remarks>
    internal ManagedDocumentScopeLease CreateDocument(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        if (!typeof(IPluginDocument).IsAssignableFrom(modelType))
        {
            throw new InvalidOperationException(
                $"声明式 Document 模型 {modelType.FullName} 未实现 IPluginDocument。");
        }

        var model = CreateScopedModel(modelType);
        DocumentLifetime lifetime;
        lock (_syncRoot)
        {
            if (!_scopes.TryGetValue(model, out var scopeLease))
            {
                throw new InvalidOperationException("Document Scope 在创建完成后丢失了所有权登记。");
            }

            lifetime = scopeLease.Lifetime;
        }

        // 对外只暴露模型、只读令牌和幂等释放入口，调用者既不能取得 IServiceScope，
        // 也不能主动触发属于 Host 的 CancellationTokenSource。
        return new ManagedDocumentScopeLease(model, lifetime.ClosingToken, this);
    }

    /// <summary>
    /// 在独立 DI Scope 中解析精确模型，并在成功返回前登记唯一所有权。
    /// </summary>
    private IPluginDocument CreateScopedModel(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Scope 在成功登记之前始终由局部变量拥有。解析构造函数失败、重复返回同一 Document，
        // 或宿主正在退出时，finally 都会立即释放已创建的服务，绝不留下半注册作用域。
        IServiceScope? scope = _scopeFactory.CreateScope();
        DocumentLifetime? lifetime = null;
        try
        {
            // 生产组合根保证每个 Scope 只有一个 DocumentLifetime。缺失注册属于组合错误，
            // 必须立即失败，不能创建第二个回退令牌破坏“唯一关闭事实源”。
            lifetime = scope.ServiceProvider.GetRequiredService<DocumentLifetime>();
            var document = (IPluginDocument)scope.ServiceProvider.GetRequiredService(modelType);
            lock (_syncRoot)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_scopes.TryAdd(document, new DocumentScopeLease(scope, lifetime)))
                {
                    throw new InvalidOperationException(
                        $"Document {modelType.FullName} 已经关联到一个依赖注入作用域。");
                }
            }

            scope = null;
            lifetime = null;
            return document;
        }
        finally
        {
            if (scope is not null)
            {
                try
                {
                    lifetime?.RequestClose();
                }
                finally
                {
                    try
                    {
                        scope.Dispose();
                    }
                    finally
                    {
                        lifetime?.Dispose();
                    }
                }
            }
        }
    }

    /// <summary>
    /// 释放指定 Document 对应的作用域。非托管 Document 和重复释放均安全返回 false。
    /// </summary>
    internal bool Release(object document)
    {
        ArgumentNullException.ThrowIfNull(document);

        DocumentScopeLease? lease;
        lock (_syncRoot)
        {
            if (!_scopes.Remove(document, out lease))
            {
                return false;
            }
        }

        // 不在锁内执行用户代码的 Dispose，避免某个服务释放时回调宿主造成锁重入或阻塞其他关闭操作。
        lease.Release();
        return true;
    }

    public void Dispose()
    {
        DocumentScopeLease[] remainingScopes;
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            remainingScopes = _scopes.Values.ToArray();
            _scopes.Clear();
        }

        // 正常情况下 Document 会在关闭时逐个释放；这里处理宿主退出时仍保持打开的 Document。
        // 逆序释放更符合“后创建的界面先退出”的直觉，也减少后创建对象引用先创建对象时的风险。
        List<Exception>? releaseFailures = null;
        for (var index = remainingScopes.Length - 1; index >= 0; index--)
        {
            try
            {
                remainingScopes[index].Release();
            }
            catch (Exception exception)
            {
                // 一个插件模型释放失败不能阻断其他 Document。完成全部清理后再统一报告，
                // 既保留异常可见性，也不把后续 Scope 留给已经退出的 Runtime。
                (releaseFailures ??= []).Add(exception);
            }
        }

        if (releaseFailures is not null)
        {
            throw new AggregateException("一个或多个 Document Scope 释放失败。", releaseFailures);
        }
    }

    private sealed class DocumentScopeLease(IServiceScope scope, DocumentLifetime lifetime)
    {
        private int _released;

        internal DocumentLifetime Lifetime { get; } = lifetime;

        internal void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            try
            {
                Lifetime.RequestClose();
            }
            finally
            {
                try
                {
                    scope.Dispose();
                }
                finally
                {
                    // Lifetime 会随 Scope 释放一次；这里再次调用只是幂等兜底，确保容器释放
                    // 某个模型时抛出异常也不会让取消源残留。
                    Lifetime.Dispose();
                }
            }
        }
    }
}

/// <summary>保存一个普通插件 Document 模型及其 Scope 的唯一释放权。</summary>
/// <remarks>
/// Lease 不暴露 DI Scope 或取消源。异步初始化、内容捕获和插件自身后台任务只观察同一个
/// <see cref="ClosingToken"/>；最终释放始终回到 <see cref="DocumentScopeManager"/>，从而保持
/// “先取消、后 Dispose Scope”的固定顺序。
/// </remarks>
internal sealed class ManagedDocumentScopeLease(
    IPluginDocument model,
    CancellationToken closingToken,
    DocumentScopeManager owner) : IDisposable
{
    private int _disposed;

    internal IPluginDocument Model { get; } = model;
    internal CancellationToken ClosingToken { get; } = closingToken;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            owner.Release(Model);
        }
    }
}
