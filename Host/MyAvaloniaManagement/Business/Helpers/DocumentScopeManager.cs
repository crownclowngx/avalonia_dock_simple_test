using System;
using System.Collections.Generic;
using System.Linq;
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
public sealed class DocumentScopeManager : IDocumentScopeFactory, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly object _syncRoot = new();
    private readonly Dictionary<Document, IServiceScope> _scopes =
        new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public DocumentScopeManager(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    /// <inheritdoc />
    public TDocument CreateDocument<TDocument>() where TDocument : Document
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Scope 在成功登记之前始终由局部变量拥有。解析构造函数失败、重复返回同一 Document，
        // 或宿主正在退出时，finally 都会立即释放已创建的服务，绝不留下半注册作用域。
        IServiceScope? scope = _scopeFactory.CreateScope();
        try
        {
            var document = scope.ServiceProvider.GetRequiredService<TDocument>();
            lock (_syncRoot)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_scopes.TryAdd(document, scope))
                {
                    throw new InvalidOperationException(
                        $"Document {typeof(TDocument).FullName} 已经关联到一个依赖注入作用域。");
                }
            }

            scope = null;
            return document;
        }
        finally
        {
            scope?.Dispose();
        }
    }

    /// <summary>
    /// 释放指定 Document 对应的作用域。非托管 Document 和重复释放均安全返回 false。
    /// </summary>
    public bool Release(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        IServiceScope? scope;
        lock (_syncRoot)
        {
            if (!_scopes.Remove(document, out scope))
            {
                return false;
            }
        }

        // 不在锁内执行用户代码的 Dispose，避免某个服务释放时回调宿主造成锁重入或阻塞其他关闭操作。
        scope.Dispose();
        return true;
    }

    public void Dispose()
    {
        IServiceScope[] remainingScopes;
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
        for (var index = remainingScopes.Length - 1; index >= 0; index--)
        {
            remainingScopes[index].Dispose();
        }
    }
}
