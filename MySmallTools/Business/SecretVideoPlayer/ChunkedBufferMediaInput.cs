using LibVLCSharp.Shared;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MySmallTools.Business.SecretVideoPlayer
{
    /// <summary>
    /// 分块缓冲的MediaInput实现
    /// 策略：预加载10秒数据块，边播放边加载下一块，支持跳转时重新加载
    /// </summary>
    public class ChunkedBufferMediaInput : MediaInput
    {
        private readonly Stream _sourceStream;
        private readonly long _totalLength;
        private readonly int _chunkSizeBytes;
        private readonly object _lockObject = new object();
        
        // 当前缓冲区和位置
        private byte[] _currentChunk;
        private long _currentChunkStartPosition;
        private long _currentPosition;
        private bool _isDisposed;
        
        // 预加载相关
        private readonly ConcurrentDictionary<long, byte[]> _preloadedChunks = new();
        private CancellationTokenSource _preloadCancellation;
        private Task _preloadTask;
        
        // 估算的比特率（用于计算10秒对应的字节数）
        private long _estimatedBitrate;
        private const int DEFAULT_CHUNK_SIZE = 10 * 1024 * 1024; // 默认10MB块
        
        public ChunkedBufferMediaInput(Stream sourceStream, long estimatedBitrate = 0)
        {
            _sourceStream = sourceStream ?? throw new ArgumentNullException(nameof(sourceStream));
            _totalLength = sourceStream.Length;
            _currentPosition = 0;
            _currentChunkStartPosition = -1;
            
            // 设置MediaInput的基本属性
            CanSeek = true;
            
            // 计算块大小：如果有比特率信息，按10秒计算；否则使用默认值
            if (estimatedBitrate > 0)
            {
                _estimatedBitrate = estimatedBitrate;
                // 10秒的数据量 = (比特率 * 10秒) / 8位每字节
                _chunkSizeBytes = Math.Max((int)(estimatedBitrate * 10 / 8), 1024 * 1024); // 最小1MB
            }
            else
            {
                _chunkSizeBytes = DEFAULT_CHUNK_SIZE;
            }
            
            _preloadCancellation = new CancellationTokenSource();
        }

        public override bool Open(out ulong size)
        {
            size = (ulong)_totalLength;
            
            // 预加载第一个块
            LoadChunk(0);
            
            // 启动预加载任务
            StartPreloadTask();
            
            return true;
        }

        public override int Read(IntPtr buf, uint len)
        {
            if (_isDisposed || buf == IntPtr.Zero || len == 0)
                return -1;

            lock (_lockObject)
            {
                try
                {
                    // 确保当前块已加载
                    EnsureCurrentChunkLoaded();
                    
                    if (_currentChunk == null)
                        return -1;

                    // 计算在当前块中的偏移
                    var offsetInChunk = (int)(_currentPosition - _currentChunkStartPosition);
                    var availableInChunk = _currentChunk.Length - offsetInChunk;
                    
                    if (availableInChunk <= 0)
                    {
                        // 当前块已读完，尝试加载下一块
                        var nextChunkStart = _currentChunkStartPosition + _currentChunk.Length;
                        if (nextChunkStart >= _totalLength)
                            return 0; // EOF
                            
                        LoadChunk(nextChunkStart);
                        if (_currentChunk == null)
                            return -1;
                            
                        offsetInChunk = 0;
                        availableInChunk = _currentChunk.Length;
                    }

                    // 读取数据
                    var bytesToRead = Math.Min((int)len, availableInChunk);
                    
                    // 使用Marshal.Copy进行内存复制
                    Marshal.Copy(_currentChunk, offsetInChunk, buf, bytesToRead);
                    
                    _currentPosition += bytesToRead;
                    
                    // 触发预加载下一块
                    TriggerPreloadNext();
                    
                    return bytesToRead;
                }
                catch (Exception)
                {
                    return -1;
                }
            }
        }

        public override bool Seek(ulong offset)
        {
            if (_isDisposed || offset > (ulong)_totalLength)
                return false;

            lock (_lockObject)
            {
                _currentPosition = (long)offset;
                
                // 检查是否需要加载新的块
                if (_currentChunk == null || 
                    _currentPosition < _currentChunkStartPosition || 
                    _currentPosition >= _currentChunkStartPosition + _currentChunk.Length)
                {
                    // 需要加载新块，计算块的起始位置
                    var chunkStart = (_currentPosition / _chunkSizeBytes) * _chunkSizeBytes;
                    LoadChunk(chunkStart);
                    
                    // 清理预加载缓存（跳转后之前的预加载可能无用）
                    ClearPreloadCache();
                    
                    // 重新启动预加载
                    TriggerPreloadNext();
                }
                
                return true;
            }
        }

        public override void Close()
        {
            Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (!_isDisposed && disposing)
            {
                _isDisposed = true;
                
                // 停止预加载任务
                _preloadCancellation?.Cancel();
                _preloadTask?.Wait(1000); // 等待最多1秒
                
                // 清理资源
                _preloadCancellation?.Dispose();
                _preloadedChunks.Clear();
                _currentChunk = null;
                
                _sourceStream?.Dispose();
            }
            
            base.Dispose(disposing);
        }

        private void EnsureCurrentChunkLoaded()
        {
            if (_currentChunk == null || 
                _currentPosition < _currentChunkStartPosition || 
                _currentPosition >= _currentChunkStartPosition + _currentChunk.Length)
            {
                var chunkStart = (_currentPosition / _chunkSizeBytes) * _chunkSizeBytes;
                LoadChunk(chunkStart);
            }
        }

        private void LoadChunk(long startPosition)
        {
            try
            {
                // 首先检查预加载缓存
                if (_preloadedChunks.TryRemove(startPosition, out var preloadedChunk))
                {
                    _currentChunk = preloadedChunk;
                    _currentChunkStartPosition = startPosition;
                    return;
                }

                // 从源流加载
                var chunkSize = Math.Min(_chunkSizeBytes, (int)(_totalLength - startPosition));
                if (chunkSize <= 0)
                {
                    _currentChunk = null;
                    return;
                }

                var chunk = new byte[chunkSize];
                _sourceStream.Position = startPosition;
                var bytesRead = _sourceStream.Read(chunk, 0, chunkSize);
                
                if (bytesRead < chunkSize)
                {
                    // 调整数组大小
                    Array.Resize(ref chunk, bytesRead);
                }

                _currentChunk = chunk;
                _currentChunkStartPosition = startPosition;
            }
            catch (Exception)
            {
                _currentChunk = null;
                _currentChunkStartPosition = -1;
            }
        }

        private void StartPreloadTask()
        {
            _preloadTask = Task.Run(async () =>
            {
                while (!_preloadCancellation.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(100, _preloadCancellation.Token); // 检查间隔
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, _preloadCancellation.Token);
        }

        private void TriggerPreloadNext()
        {
            if (_currentChunk == null) return;

            Task.Run(() =>
            {
                try
                {
                    var nextChunkStart = _currentChunkStartPosition + _currentChunk.Length;
                    if (nextChunkStart < _totalLength && !_preloadedChunks.ContainsKey(nextChunkStart))
                    {
                        PreloadChunk(nextChunkStart);
                    }
                }
                catch (Exception)
                {
                    // 预加载失败不影响主流程
                }
            }, _preloadCancellation.Token);
        }

        private void PreloadChunk(long startPosition)
        {
            try
            {
                var chunkSize = Math.Min(_chunkSizeBytes, (int)(_totalLength - startPosition));
                if (chunkSize <= 0) return;

                var chunk = new byte[chunkSize];
                
                // 创建新的流位置（不影响主读取）
                lock (_sourceStream)
                {
                    var originalPosition = _sourceStream.Position;
                    _sourceStream.Position = startPosition;
                    var bytesRead = _sourceStream.Read(chunk, 0, chunkSize);
                    _sourceStream.Position = originalPosition; // 恢复原位置
                    
                    if (bytesRead < chunkSize)
                    {
                        Array.Resize(ref chunk, bytesRead);
                    }
                }

                // 限制预加载缓存大小（最多3个块）
                if (_preloadedChunks.Count >= 3)
                {
                    // 移除最旧的块
                    var oldestKey = long.MaxValue;
                    foreach (var key in _preloadedChunks.Keys)
                    {
                        if (key < oldestKey) oldestKey = key;
                    }
                    if (oldestKey != long.MaxValue)
                    {
                        _preloadedChunks.TryRemove(oldestKey, out _);
                    }
                }

                _preloadedChunks.TryAdd(startPosition, chunk);
            }
            catch (Exception)
            {
                // 预加载失败不影响主流程
            }
        }

        private void ClearPreloadCache()
        {
            _preloadedChunks.Clear();
        }
    }
}