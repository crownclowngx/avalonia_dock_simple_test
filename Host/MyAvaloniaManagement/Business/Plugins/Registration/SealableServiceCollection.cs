using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace MyAvaloniaManagement.Business.Plugins.Registration;

/// <summary>
/// 为插件私有 <see cref="IServiceCollection"/> 增加一次性封闭语义。
/// </summary>
/// <remarks>
/// UI SDK 需要保留标准 DI 的完整表达力，因此没有再造服务注册 DSL。本包装器只委托
/// <see cref="IList{T}"/>，并在模块返回后拒绝所有写操作；读取仍然开放，便于宿主构建独立
/// Provider。它不是容器、事务或服务定位器，只是一道明确的组合期写入边界。
/// </remarks>
internal sealed class SealableServiceCollection(IServiceCollection inner) : IServiceCollection
{
    private readonly IServiceCollection _inner =
        inner ?? throw new ArgumentNullException(nameof(inner));
    private bool _sealed;

    public ServiceDescriptor this[int index]
    {
        get => _inner[index];
        set
        {
            EnsureWritable();
            _inner[index] = value;
        }
    }

    public int Count => _inner.Count;
    public bool IsReadOnly => _sealed || _inner.IsReadOnly;

    public void Add(ServiceDescriptor item)
    {
        EnsureWritable();
        _inner.Add(item);
    }

    public void Clear()
    {
        EnsureWritable();
        _inner.Clear();
    }

    public bool Contains(ServiceDescriptor item) => _inner.Contains(item);
    public void CopyTo(ServiceDescriptor[] array, int arrayIndex) =>
        _inner.CopyTo(array, arrayIndex);
    public IEnumerator<ServiceDescriptor> GetEnumerator() => _inner.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(ServiceDescriptor item) => _inner.IndexOf(item);

    public void Insert(int index, ServiceDescriptor item)
    {
        EnsureWritable();
        _inner.Insert(index, item);
    }

    public bool Remove(ServiceDescriptor item)
    {
        EnsureWritable();
        return _inner.Remove(item);
    }

    public void RemoveAt(int index)
    {
        EnsureWritable();
        _inner.RemoveAt(index);
    }

    /// <summary>永久关闭当前包装器的写能力；重复调用保持幂等。</summary>
    internal void Seal() => _sealed = true;

    private void EnsureWritable()
    {
        if (_sealed)
        {
            throw new InvalidOperationException(
                "插件注册入口已经封闭，不能在模块返回后修改私有服务集合。");
        }
    }
}
