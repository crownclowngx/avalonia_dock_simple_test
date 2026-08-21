using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 统一管理由依赖注入容器创建的 Document 作用域。
/// </summary>
/// <remarks>
/// Document 的可见生命周期由 Dock 决定，而服务的生命周期由 Microsoft DI 决定。
/// 本类是两者之间唯一的所有权桥梁：创建时保存 Document 与 Scope 的一一对应关系，
/// Dock 真正确认关闭后再释放 Scope。插件不能自行保存或释放 Scope，避免提前释放、重复释放，
/// 以及从根容器解析可释放 transient 后一直存活到进程退出的问题。
/// </remarks>
internal sealed class DocumentScopeManager : IDocumentScopeFactory, IDisposable
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
    public TDocument CreateDocument<TDocument>() where TDocument : Document
        => (TDocument)CreateScopedModel(typeof(TDocument), requirePluginDocument: false);

    /// <summary>
    /// 按声明式 Registry 已验证的精确模型类型创建并托管普通插件 Document。
    /// </summary>
    /// <remarks>
    /// 本入口不扫描类型、不接受任意服务名，也不要求模型继承 Dock。返回模型随后由 Host internal
    /// Adapter 承载；插件始终只观察自己的业务对象和 ClosingToken。
    /// </remarks>
    internal MyAvaloniaManagement.PluginSdk.IPluginDocument CreatePluginDocument(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        if (!typeof(MyAvaloniaManagement.PluginSdk.IPluginDocument).IsAssignableFrom(modelType))
        {
            throw new InvalidOperationException(
                $"声明式 Document 模型 {modelType.FullName} 未实现 IPluginDocument。");
        }

        return (MyAvaloniaManagement.PluginSdk.IPluginDocument)CreateScopedModel(
            modelType,
            requirePluginDocument: true);
    }

    /// <summary>仅供 G7 前仓库内旧持久化测试创建 Dock Document；生产组合不调用。</summary>
    internal Document CreateLegacyDocument(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        if (!typeof(Document).IsAssignableFrom(modelType))
        {
            throw new InvalidOperationException($"旧测试模型 {modelType.FullName} 不是 Dock Document。");
        }

        return (Document)CreateScopedModel(modelType, requirePluginDocument: false);
    }

    /// <summary>
    /// 在独立 DI Scope 中解析精确模型，并在成功返回前登记唯一所有权。
    /// </summary>
    /// <remarks>
    /// G6 的生产入口要求普通 <c>IPluginDocument</c>；旧泛型入口只为 G7 前的仓库回归夹具保留。
    /// 两条入口最终汇入同一个租约实现，因此关闭取消、异常回滚和宿主退出不会形成两套释放算法。
    /// </remarks>
    private object CreateScopedModel(Type modelType, bool requirePluginDocument)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (requirePluginDocument &&
            !typeof(MyAvaloniaManagement.PluginSdk.IPluginDocument).IsAssignableFrom(modelType))
        {
            throw new InvalidOperationException(
                $"声明式 Document 模型 {modelType.FullName} 未实现 IPluginDocument。");
        }

        // Scope 在成功登记之前始终由局部变量拥有。解析构造函数失败、重复返回同一 Document，
        // 或宿主正在退出时，finally 都会立即释放已创建的服务，绝不留下半注册作用域。
        IServiceScope? scope = _scopeFactory.CreateScope();
        DocumentLifetime? lifetime = null;
        try
        {
            // 正式运行时，宿主会在每个 Document Scope 中注册唯一的 DocumentLifetime，
            // 因而 Document 及其 scoped 依赖解析到的是同一个关闭信号。这里保留回退实例，
            // 是为了兼容只注册 DocumentScopeManager 的轻量测试与旧组合入口；回退只影响
            // 未注入 IDocumentLifetime 的旧对象，不会在正式路径中形成第二套生命周期事实源。
            lifetime = scope.ServiceProvider.GetService<DocumentLifetime>() ?? new DocumentLifetime();
            var document = scope.ServiceProvider.GetRequiredService(modelType);
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
    public bool Release(object document)
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

        internal void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            try
            {
                lifetime.RequestClose();
            }
            finally
            {
                try
                {
                    scope.Dispose();
                }
                finally
                {
                    // 回退 Lifetime 没有被 DI Scope 捕获，必须由租约显式释放；正式 Lifetime
                    // 会随 Scope 再释放一次。DocumentLifetime.Dispose 保证幂等，因此统一调用
                    // 可以同时覆盖两条构造路径，而不需要把“是否来自容器”泄漏到释放主流程。
                    lifetime.Dispose();
                }
            }
        }
    }
}
