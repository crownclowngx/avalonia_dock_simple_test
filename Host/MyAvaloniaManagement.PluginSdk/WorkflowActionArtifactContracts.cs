namespace MyAvaloniaManagement.PluginSdk;

/// <summary>Host 签发的本次调用产物引用。只有调用成功后才能读取。</summary>
public sealed class WorkflowActionArtifactReference(string id, string mediaType, long sizeBytes)
{
    /// <summary>取得当前 Run 内的产物 ID。</summary>
    public string Id { get; } = id;
    /// <summary>取得产物的媒体类型。</summary>
    public string MediaType { get; } = mediaType;
    /// <summary>取得实际写入的字节数。</summary>
    public long SizeBytes { get; } = sizeBytes;
}

/// <summary>一次调用最多写入一个内存产物。写入按顺序进行。</summary>
public interface IWorkflowActionArtifactWriter
{
    /// <summary>将字节复制到当前调用的 Host 内存缓冲。</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);

    /// <summary>封存内容，约定结果顶层属性包含 id、mediaType 和 sizeBytes。</summary>
    WorkflowActionArtifactReference Seal(string outputProperty, string mediaType);
}

/// <summary>由 Host 的 Workflow Run 额外实现。读取权限和流的有效期均绑定到该 Run。</summary>
public interface IWorkflowActionArtifactReader
{
    /// <summary>从位置零打开当前 Run 中已经发布的产物。调用者应释放读取流。</summary>
    ValueTask<Stream> OpenReadAsync(string artifactId, CancellationToken cancellationToken);
}
