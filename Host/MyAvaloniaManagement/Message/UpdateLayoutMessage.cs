using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MyAvaloniaManagement.Message;

internal sealed class UpdateLayoutMessage : ValueChangedMessage<string>
{
    public UpdateLayoutMessage(string value) : base(value)
    {
    }
}
