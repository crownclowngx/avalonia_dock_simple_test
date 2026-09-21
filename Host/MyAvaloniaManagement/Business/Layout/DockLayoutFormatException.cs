using System;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 表示布局线格式或结构不符合当前协议。编解码器与结构校验共用稳定错误码，
/// Store 据此区分损坏输入和未来格式；异常不拥有文件，也不决定隔离或只读策略。
/// 消息只携带错误码，可选身份用于定位结构项，不包含原始 JSON、业务内容或内部解析异常。
/// </summary>
internal sealed class DockLayoutFormatException(string code, string? stableId = null) : Exception(code)
{
    internal string Code { get; } = code;
    internal string? StableId { get; } = stableId;
}
