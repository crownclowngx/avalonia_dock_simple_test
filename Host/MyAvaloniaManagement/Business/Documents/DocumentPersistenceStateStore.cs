using System;
using System.Collections.Generic;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Helpers;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>
/// 保存宿主对每个可持久化 Document 的运行期所有权事实。
/// </summary>
/// <remarks>
/// <para>
/// 插件只实现内容快照契约，不能再自报 Document 类型或持有主文件路径。本存储在创建时把
/// Document 实例与不可变 Plugin Registry 注册项绑定，并在主文件成功提交后记录规范路径。
/// 保存、关闭、重复打开和恢复流程因此读取同一份宿主事实，不会因插件属性漂移而产生分叉。
/// </para>
/// <para>
/// 这里刻意只使用按引用比较的字典，而不引入仓储、事件或状态机。Document 数量有限，路径
/// 查重由工作区遍历完成；保持数据结构简单可以让创建与释放的生命周期边界直接可见。
/// </para>
/// </remarks>
internal sealed class DocumentPersistenceStateStore
{
    private readonly Dictionary<Document, DocumentPersistenceState> _states =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// 登记一个已经通过公共保存契约校验的 Document。
    /// </summary>
    /// <remarks>
    /// 同一对象被策略重复返回属于所有权错误，必须立即拒绝；静默覆盖会让旧 Dock 标签与新创建
    /// 请求共享路径和关闭状态，最终可能保存到错误文件。
    /// </remarks>
    internal void Register(Document document, PluginDocumentRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(registration);
        if (!_states.TryAdd(document, new DocumentPersistenceState(registration)))
        {
            throw new InvalidOperationException("同一个 Document 实例不能重复登记持久化状态。");
        }
    }

    /// <summary>尝试取得宿主登记的不可变所有权和当前主文件路径。</summary>
    internal bool TryGet(Document document, out DocumentPersistenceState state) =>
        _states.TryGetValue(document, out state!);

    /// <summary>
    /// 在内容恢复完成或主文件原子提交成功后更新当前主文件路径。
    /// </summary>
    /// <remarks>
    /// 调用方负责传入已经由 <see cref="DocumentPathIdentity"/> 规范化的非空路径。
    /// 空字符串只由 <see cref="ClearFilePath"/> 表示“新建或恢复后必须另存”。
    /// </remarks>
    internal void CommitFilePath(Document document, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        GetRequired(document).FilePath = DocumentPathIdentity.Normalize(filePath);
    }

    /// <summary>清空主文件路径，使恢复副本在下一次保存时强制选择新位置。</summary>
    internal void ClearFilePath(Document document) => GetRequired(document).FilePath = string.Empty;

    /// <summary>在 Document 所有权结束时幂等删除运行期状态。</summary>
    internal bool Remove(Document document) => _states.Remove(document);

    private DocumentPersistenceState GetRequired(Document document) =>
        _states.TryGetValue(document, out var state)
            ? state
            : throw new InvalidOperationException("该 Document 没有宿主持久化状态登记。");
}

/// <summary>宿主为单个 Document 保存的最小运行期持久化状态。</summary>
internal sealed class DocumentPersistenceState(PluginDocumentRegistration registration)
{
    /// <summary>获取创建该 Document 的规范 Registry 注册项。</summary>
    internal PluginDocumentRegistration Registration { get; } = registration;

    /// <summary>获取或设置最近一次成功恢复或提交的规范主文件路径。</summary>
    internal string FilePath { get; set; } = string.Empty;
}
