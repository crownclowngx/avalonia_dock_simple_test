using System;
using System.Collections.Generic;
using Dock.Model.Mvvm.Controls;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 记录本次宿主运行期内所有 Document Scope 管理器，并把关闭请求路由到真正拥有该 Document 的容器。
/// </summary>
/// <remarks>
/// <para>
/// G4 以后宿主和每个插件分别拥有独立 Provider，因此不再存在一个能够释放全部 Document 的根级
/// <see cref="DocumentScopeManager"/>。本类型只保存所有者列表，不创建 Scope、也不拥有 Provider；
/// 它的单一职责是把宿主 Dock 的关闭通知转发给正确的所有者。
/// </para>
/// <para>
/// 管理器按创建顺序登记，宿主退出时按相反顺序关闭剩余 Scope。这样后创建插件的 Document 先退出，
/// 随后生命周期停止、插件 Provider 释放，最后才释放宿主 Provider，所有权链条保持清晰且可测试。
/// </para>
/// </remarks>
internal sealed class DocumentScopeRegistry
{
    private readonly object _syncRoot = new();
    private readonly List<DocumentScopeManager> _managers = [];
    private bool _closing;

    /// <summary>获取已提交所有者数量，仅供组合测试验证失败候选没有登记 Scope。</summary>
    internal int ManagerCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _managers.Count;
            }
        }
    }

    /// <summary>登记一个刚刚成功建立的容器所拥有的 Document Scope 管理器。</summary>
    internal void Register(DocumentScopeManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        lock (_syncRoot)
        {
            if (_closing)
            {
                throw new InvalidOperationException("宿主已经开始关闭，不能再登记 Document Scope 管理器。");
            }

            if (!_managers.Exists(item => ReferenceEquals(item, manager)))
            {
                _managers.Add(manager);
            }
        }
    }

    /// <summary>尝试由实际所有者释放指定 Document；非托管对象安全返回 false。</summary>
    internal bool Release(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        DocumentScopeManager[] snapshot;
        lock (_syncRoot)
        {
            snapshot = [.. _managers];
        }

        for (var index = snapshot.Length - 1; index >= 0; index--)
        {
            if (snapshot[index].Release(document))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>宿主退出时，按容器建立顺序的反方向关闭全部仍存活的 Document Scope。</summary>
    internal void CloseAll()
    {
        DocumentScopeManager[] snapshot;
        lock (_syncRoot)
        {
            if (_closing)
            {
                return;
            }

            _closing = true;
            snapshot = [.. _managers];
        }

        List<Exception>? closeFailures = null;
        for (var index = snapshot.Length - 1; index >= 0; index--)
        {
            try
            {
                snapshot[index].Dispose();
            }
            catch (Exception exception)
            {
                // 某个插件的 Scope 释放异常不能阻断其他插件。全部管理器尝试完成后，
                // 再把异常交给 Runtime 诊断/退出边界处理。
                (closeFailures ??= []).Add(exception);
            }
        }

        if (closeFailures is not null)
        {
            throw new AggregateException("一个或多个 Document Scope 所有者关闭失败。", closeFailures);
        }
    }
}
