using System;
using System.Collections.Generic;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Helpers;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>保存 Host 对每个 V2 Document 的最小运行期持久化事实。</summary>
/// <remarks>
/// 插件只报告内容与脏状态，不能持有路径、磁盘标题或恢复决策。本存储按 Adapter 引用绑定冻结的
/// Registry 注册项，并让打开、保存、关闭和路径查重读取同一事实源。
/// </remarks>
internal sealed class DocumentPersistenceStateStore
{
    private readonly Dictionary<ManagedDocumentDockable, DocumentPersistenceState> _states =
        new(ReferenceEqualityComparer.Instance);

    internal void Register(ManagedDocumentDockable document, string hostTitle)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostTitle);
        if (document.PluginRegistration is not { IsPersistable: true } pluginRegistration ||
            document.PersistableModel is null)
        {
            throw new InvalidOperationException("只有声明为可持久化的 V2 Document 才能登记磁盘状态。");
        }

        if (!_states.TryAdd(
                document,
                new DocumentPersistenceState(pluginRegistration, hostTitle)))
        {
            throw new InvalidOperationException("同一个 Document Adapter 不能重复登记持久化状态。");
        }
    }

    internal bool TryGet(
        ManagedDocumentDockable document,
        out DocumentPersistenceState state) =>
        _states.TryGetValue(document, out state!);

    internal void CommitFile(
        ManagedDocumentDockable document,
        string filePath,
        string hostTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostTitle);
        var state = GetRequired(document);
        state.FilePath = DocumentPathIdentity.Normalize(filePath);
        state.HostTitle = hostTitle;
        state.RequiresSave = false;
        document.CommitHostTitle(hostTitle);
        document.SetHostRequiresSave(false);
    }

    /// <summary>把恢复副本标记为必须另存，避免依赖插件是否把恢复内容报告为脏。</summary>
    internal void MarkRecovered(ManagedDocumentDockable document, string hostTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostTitle);
        var state = GetRequired(document);
        state.FilePath = string.Empty;
        state.HostTitle = hostTitle;
        state.RequiresSave = true;
        document.CommitHostTitle(hostTitle);
        document.SetHostRequiresSave(true);
    }

    internal bool IsDirty(ManagedDocumentDockable document) =>
        TryGet(document, out var state) &&
        (state.RequiresSave || document.PersistableModel?.IsDirty == true);

    internal bool Remove(ManagedDocumentDockable document) => _states.Remove(document);

    private DocumentPersistenceState GetRequired(ManagedDocumentDockable document) =>
        _states.TryGetValue(document, out var state)
            ? state
            : throw new InvalidOperationException("该 Document 没有匹配的 Host 持久化状态。");
}

/// <summary>Host 为一个可持久化 Document 保存的运行期事实。</summary>
internal sealed class DocumentPersistenceState(
    PluginDocumentRegistration registration,
    string hostTitle)
{
    internal PluginDocumentRegistration Registration { get; } = registration;
    internal string FilePath { get; set; } = string.Empty;
    internal string HostTitle { get; set; } = hostTitle;
    internal bool RequiresSave { get; set; }
}
