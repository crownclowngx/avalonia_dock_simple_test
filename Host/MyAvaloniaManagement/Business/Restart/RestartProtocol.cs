using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Restart;

/// <summary>一次性交接的固定长度消息；不解析任意 JSON、路径或执行指令。</summary>
internal static class RestartProtocol
{
    internal const byte Ready = 1, CleanExit = 2, Failed = 3;
    internal static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(90);

    internal static async Task SendAsync(Stream stream, byte value, CancellationToken token)
    {
        await stream.WriteAsync(new[] { value }, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    internal static async Task<byte> ReadAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[1];
        await stream.ReadExactlyAsync(buffer, token).ConfigureAwait(false);
        return buffer[0];
    }

    internal static async Task AuthenticateAsync(Stream stream, string identity, CancellationToken token)
    {
        var buffer = new byte[32];
        await stream.ReadExactlyAsync(buffer, token).ConfigureAwait(false);
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(buffer, Encoding.ASCII.GetBytes(identity)))
            throw new InvalidDataException("重启会话身份不匹配。");
    }
}
