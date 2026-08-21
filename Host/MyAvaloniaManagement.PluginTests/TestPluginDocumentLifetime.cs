using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 为直接构造 V2 Document 模型的单元测试提供可控关闭端口。
/// </summary>
/// <remarks>
/// 生产关闭信号由 Host internal Scope 拥有；测试替身只复制 SDK 的只读观察语义，不能被生产插件引用。
/// </remarks>
internal sealed class TestPluginDocumentLifetime : IDocumentLifetime, IDisposable
{
    private readonly CancellationTokenSource _source = new();

    public CancellationToken ClosingToken => _source.Token;

    public bool IsClosing => _source.IsCancellationRequested;

    internal void Close() => _source.Cancel();

    public void Dispose() => _source.Dispose();
}
