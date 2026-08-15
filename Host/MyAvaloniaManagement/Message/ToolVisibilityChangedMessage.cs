using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MyAvaloniaManagement.Message;

/// <summary>
/// 工具可见性变更消息，当工具被隐藏或恢复时触发
/// </summary>
internal sealed class ToolVisibilityChangedMessage : ValueChangedMessage<string>
{
    public ToolVisibilityChangedMessage(string value) : base(value)
    {
    }
}
