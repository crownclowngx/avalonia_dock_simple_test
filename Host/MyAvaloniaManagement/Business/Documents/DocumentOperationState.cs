using System;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>
/// 保存当前 HostRuntime 的文档操作提示，并在提示真正变化时通知主窗口刷新绑定。
/// </summary>
/// <remarks>
/// 该状态属于宿主根容器，而不是某个瞬态主窗口。文件菜单和文件树虽然是两个入口，
/// 但它们操作的是同一文档工作区，因此必须共享同一份错误提示事实。这里刻意不使用
/// 公共事件总线：通知只在状态拥有者与主窗口之间发生，不需要事件类型、路由或多消费者协议。
/// </remarks>
internal sealed class DocumentOperationState
{
    private string _error = string.Empty;

    /// <summary>当用户可见的文档操作提示发生变化时触发。</summary>
    internal event EventHandler? Changed;

    /// <summary>获取已经过宿主归一化、可以安全展示给用户的错误文本。</summary>
    internal string Error => _error;

    /// <summary>获取当前是否存在需要展示的文档操作错误。</summary>
    internal bool HasError => !string.IsNullOrWhiteSpace(_error);

    /// <summary>
    /// 应用协调器返回的状态更新意图。取消或无操作结果不会覆盖已有错误。
    /// </summary>
    internal void Apply(DocumentOperationResult result)
    {
        if (result.ShouldUpdateError)
        {
            SetError(result.Error);
        }
    }

    /// <summary>
    /// 记录文件树入口发生的非预期失败。界面只接收固定文本，诊断只写异常类型，
    /// 避免把路径、插件异常正文或其他敏感上下文带入长期输出。
    /// </summary>
    internal void ReportUnexpectedOpenFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        SetError("无法打开文件：宿主处理文档时发生意外错误。原文件未被修改。");
        Console.Error.WriteLine(
            $"DocumentPersistence errorCode=DOCUMENT_HOST_TOOL_OPEN_FAILED type={exception.GetType().Name}");
    }

    /// <summary>清除当前提示。若状态已经为空，则不产生重复的界面通知。</summary>
    internal void Clear() => SetError(string.Empty);

    private void SetError(string error)
    {
        error ??= string.Empty;
        if (string.Equals(_error, error, StringComparison.Ordinal))
        {
            return;
        }

        _error = error;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
