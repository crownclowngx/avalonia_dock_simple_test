using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.WorkflowActions;

internal sealed class WorkflowArtifactStore
{
    private const int BlockSize = 16 * 1024;
    private const int MaximumArtifactBytes = 8 * 1024 * 1024;
    private const int MaximumRunBytes = 16 * 1024 * 1024;
    private const int MaximumHostBytes = 64 * 1024 * 1024;

    private readonly object _gate = new();
    private int _allocated;

    internal Run CreateRun() => new(this);

    internal sealed class Run(WorkflowArtifactStore store)
    {
        private readonly Dictionary<string, Buffer> _published = new(StringComparer.Ordinal);
        private int _allocated;
        private bool _closed;

        internal Pending Begin(CancellationToken cancellationToken) => new(this, cancellationToken);

        internal ValueTask<Stream> OpenReadAsync(string id, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            cancellationToken.ThrowIfCancellationRequested();
            lock (store._gate)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
                if (!_published.TryGetValue(id, out var buffer))
                {
                    throw new KeyNotFoundException("当前 Run 中没有该产物。");
                }
                return ValueTask.FromResult<Stream>(new Reader(this, buffer));
            }
        }

        internal void Close()
        {
            lock (store._gate)
            {
                _closed = true;
                foreach (var buffer in _published.Values)
                {
                    Release(buffer);
                }
                _published.Clear();
            }
        }

        private void Release(Buffer buffer)
        {
            var bytes = buffer.Blocks.Count * BlockSize;
            buffer.Blocks.Clear();
            _allocated -= bytes;
            store._allocated -= bytes;
        }

        private sealed class Buffer
        {
            internal readonly List<byte[]> Blocks = [];
            internal int Length;
        }

        internal sealed class Pending(Run run, CancellationToken invocationCancellation) :
            IWorkflowActionArtifactWriter, IDisposable
        {
            private readonly Buffer _buffer = new();
            private WorkflowActionArtifactReference? _reference;
            private string? _outputProperty;
            private bool _failed;
            private bool _disposed;
            private bool _published;

            public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (run.StoreGate)
                {
                    EnsureWritable();
                    var remaining = bytes.Span;
                    var free = _buffer.Blocks.Count * BlockSize - _buffer.Length;
                    var blocks = (Math.Max(0L, (long)remaining.Length - free) + BlockSize - 1) / BlockSize;
                    var required = blocks * BlockSize;
                    if (remaining.Length > MaximumArtifactBytes - _buffer.Length ||
                        required > MaximumRunBytes - run._allocated ||
                        required > MaximumHostBytes - run.Store._allocated)
                    {
                        _failed = true;
                        throw new WorkflowArtifactQuotaException();
                    }
                    while (!remaining.IsEmpty)
                    {
                        var offset = _buffer.Length % BlockSize;
                        if (offset == 0)
                        {
                            _buffer.Blocks.Add(new byte[BlockSize]);
                            run._allocated += BlockSize;
                            run.Store._allocated += BlockSize;
                        }
                        var count = Math.Min(remaining.Length, BlockSize - offset);
                        remaining[..count].CopyTo(_buffer.Blocks[^1].AsSpan(offset));
                        _buffer.Length += count;
                        remaining = remaining[count..];
                    }
                }
                return ValueTask.CompletedTask;
            }

            public WorkflowActionArtifactReference Seal(string outputProperty, string mediaType)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(outputProperty);
                ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
                if (outputProperty.Length > 128 || mediaType.Length > 128)
                {
                    throw new ArgumentException("产物属性名或媒体类型超出长度限制。");
                }
                lock (run.StoreGate)
                {
                    EnsureWritable();
                    if (_buffer.Length == 0)
                    {
                        throw new InvalidOperationException("不能封存空产物。");
                    }
                    _outputProperty = outputProperty;
                    _reference = new WorkflowActionArtifactReference(Guid.NewGuid().ToString("N"), mediaType, _buffer.Length);
                    return _reference;
                }
            }

            internal void Publish(JsonElement output)
            {
                lock (run.StoreGate)
                {
                    invocationCancellation.ThrowIfCancellationRequested();
                    if (run._closed)
                    {
                        throw new OperationCanceledException(invocationCancellation);
                    }
                    if (_failed || (_reference is null && _buffer.Length != 0))
                    {
                        throw new ArgumentException("产物未成功封存。");
                    }
                    if (_reference is null)
                    {
                        return;
                    }
                    if (output.ValueKind != JsonValueKind.Object ||
                        !output.TryGetProperty(_outputProperty!, out var reference) ||
                        reference.ValueKind != JsonValueKind.Object ||
                        !Matches(reference, "id", _reference.Id) ||
                        !Matches(reference, "mediaType", _reference.MediaType) ||
                        !reference.TryGetProperty("sizeBytes", out var size) ||
                        size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var length) ||
                        length != _reference.SizeBytes)
                    {
                        throw new ArgumentException("输出必须包含本次调用签发的产物引用。");
                    }
                    run._published.Add(_reference.Id, _buffer);
                    _published = true;
                    _disposed = true;
                }
            }

            public void Dispose()
            {
                lock (run.StoreGate)
                {
                    if (!_published && !_disposed)
                    {
                        run.Release(_buffer);
                        _disposed = true;
                    }
                }
            }

            private void EnsureWritable()
            {
                invocationCancellation.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed || run._closed, this);
                if (_failed || _reference is not null)
                {
                    throw new InvalidOperationException("产物已封存或写入失败。");
                }
            }

            private static bool Matches(JsonElement reference, string property, string value) =>
                reference.TryGetProperty(property, out var field) &&
                field.ValueKind == JsonValueKind.String && field.ValueEquals(value);
        }

        private WorkflowArtifactStore Store => store;
        private object StoreGate => store._gate;

        private sealed class Reader(Run run, Buffer buffer) : Stream
        {
            private long _position;
            private bool _disposed;

            public override bool CanRead
            {
                get
                {
                    lock (run.StoreGate)
                    {
                        return !_disposed && !run._closed;
                    }
                }
            }
            public override bool CanSeek => CanRead;
            public override bool CanWrite => false;
            public override long Length => buffer.Length;
            public override long Position
            {
                get => _position;
                set => Seek(value, SeekOrigin.Begin);
            }

            public override int Read(byte[] bytes, int offset, int count)
            {
                ValidateBufferArguments(bytes, offset, count);
                return Read(bytes.AsSpan(offset, count));
            }

            public override int Read(Span<byte> destination)
            {
                lock (run.StoreGate)
                {
                    EnsureOpen();
                    var count = (int)Math.Min(destination.Length, Math.Max(0, buffer.Length - _position));
                    var remaining = destination[..count];
                    while (!remaining.IsEmpty)
                    {
                        var block = (int)(_position / BlockSize);
                        var offset = (int)(_position % BlockSize);
                        var length = Math.Min(remaining.Length, BlockSize - offset);
                        buffer.Blocks[block].AsSpan(offset, length).CopyTo(remaining);
                        _position += length;
                        remaining = remaining[length..];
                    }
                    return count;
                }
            }

            public override ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(Read(destination.Span));
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                lock (run.StoreGate)
                {
                    EnsureOpen();
                    var start = origin switch
                    {
                        SeekOrigin.Begin => 0,
                        SeekOrigin.Current => _position,
                        SeekOrigin.End => buffer.Length,
                        _ => throw new ArgumentOutOfRangeException(nameof(origin)),
                    };
                    var next = checked(start + offset);
                    if (next < 0)
                    {
                        throw new IOException("不能移动到流起点之前。");
                    }
                    return _position = next;
                }
            }

            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] bytes, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                lock (run.StoreGate)
                {
                    _disposed = true;
                }
                base.Dispose(disposing);
            }

            private void EnsureOpen() => ObjectDisposedException.ThrowIf(_disposed || run._closed, this);
        }
    }
}

internal sealed class WorkflowArtifactQuotaException() : IOException("Workflow Action 内存产物超出配额。");
