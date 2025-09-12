using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MyAvaloniaManagement.Message;

public class UpdateLayoutMessage : ValueChangedMessage<string>
{
    public UpdateLayoutMessage(string value) : base(value)
    {
    }
}