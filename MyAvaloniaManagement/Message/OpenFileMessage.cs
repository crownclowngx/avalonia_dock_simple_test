using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MyAvaloniaManagement.Message;

/// <summary>
/// 打开文件消息，用于在组件间传递打开文件的请求
/// </summary>
public class OpenFileMessage : ValueChangedMessage<string>
{
    /// <summary>
    /// 文件路径
    /// </summary>
    public string FilePath { get; }
    
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="filePath">要打开的文件路径</param>
    public OpenFileMessage(string filePath) : base(filePath)
    {
        FilePath = filePath;
    }
}