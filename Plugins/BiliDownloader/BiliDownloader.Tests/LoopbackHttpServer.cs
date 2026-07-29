using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BiliDownloader.Tests;

internal sealed record LoopbackRequest(
    string Method,
    string Target,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body)
{
    public string? Header(string name)
        => Headers.GetValueOrDefault(name);
}

internal sealed record LoopbackResponse(
    int StatusCode,
    byte[] Body,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    public static LoopbackResponse Bytes(
        byte[] body,
        int statusCode = 200,
        IReadOnlyDictionary<string, string>? headers = null)
        => new(statusCode, body, headers);

    public static LoopbackResponse Text(
        string body,
        int statusCode = 200,
        IReadOnlyDictionary<string, string>? headers = null)
        => new(statusCode, Encoding.UTF8.GetBytes(body), headers);
}

/// <summary>
/// 只监听 127.0.0.1 的最小 HTTP/1.1 测试服务器。它刻意不依赖 ASP.NET，
/// 便于精确构造 Range、Content-Length 和异常响应。
/// </summary>
internal sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Func<LoopbackRequest, Task<LoopbackResponse>> _handler;
    private readonly Task _acceptLoop;

    public LoopbackHttpServer(
        Func<LoopbackRequest, Task<LoopbackResponse>> handler)
    {
        _handler = handler;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUri = new Uri($"http://127.0.0.1:{port}/");
        _acceptLoop = AcceptLoopAsync();
    }

    public Uri BaseUri { get; }
    public ConcurrentQueue<LoopbackRequest> Requests { get; } = new();

    public string Url(string relative = "")
        => new Uri(BaseUri, relative).AbsoluteUri;

    public static LoopbackHttpServer Create(
        Func<LoopbackRequest, LoopbackResponse> handler)
        => new(request => Task.FromResult(handler(request)));

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_shutdown.Token);
                _ = HandleClientAsync(client, _shutdown.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);

            var requestLine = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }

            var requestParts = requestLine.Split(' ', 3);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrEmpty(line))
                {
                    break;
                }

                var separator = line.IndexOf(':');
                if (separator > 0)
                {
                    headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
                }
            }

            var contentLength = headers.TryGetValue("Content-Length", out var rawLength)
                && int.TryParse(rawLength, out var parsedLength)
                    ? parsedLength
                    : 0;
            var body = new byte[contentLength];
            for (var i = 0; i < contentLength; i++)
            {
                var value = reader.Read();
                if (value < 0)
                {
                    break;
                }
                body[i] = (byte)value;
            }

            var request = new LoopbackRequest(
                requestParts[0],
                requestParts.Length > 1 ? requestParts[1] : "/",
                headers,
                body);
            Requests.Enqueue(request);

            var response = await _handler(request);
            var reason = response.StatusCode switch
            {
                200 => "OK",
                206 => "Partial Content",
                302 => "Found",
                400 => "Bad Request",
                404 => "Not Found",
                416 => "Range Not Satisfiable",
                500 => "Internal Server Error",
                _ => "Test Response",
            };

            var responseHeaders = new Dictionary<string, string>(
                response.Headers ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
            responseHeaders.TryAdd("Content-Length", response.Body.Length.ToString());
            responseHeaders.TryAdd("Connection", "close");

            var headerBuilder = new StringBuilder($"HTTP/1.1 {response.StatusCode} {reason}\r\n");
            foreach (var pair in responseHeaders)
            {
                headerBuilder.Append(pair.Key).Append(": ").Append(pair.Value).Append("\r\n");
            }
            headerBuilder.Append("\r\n");

            await stream.WriteAsync(Encoding.ASCII.GetBytes(headerBuilder.ToString()), cancellationToken);
            if (!request.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
                && response.Body.Length > 0)
            {
                await stream.WriteAsync(response.Body, cancellationToken);
            }
            await stream.FlushAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _listener.Stop();
        try
        {
            await _acceptLoop;
        }
        finally
        {
            _shutdown.Dispose();
        }
    }
}
