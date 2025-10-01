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
    /// 优化的分块缓冲MediaInput - 专门针对LibVLC在可寻址模式下的随机访问优化
    /// 解决seek=true时的卡顿问题
    /// </summary>
    public class OptimizedChunkedBufferMediaInput : MediaInput
    {
        private readonly Stream _sourceStream;
        private readonly long _totalLength;
        private readonly int _chunkSizeBytes;
        private readonly object _lockObject = new object();
        
        // 多级缓存系统
        private readonly ConcurrentDictionary<long, byte[]> _hotCache = new(); // 热点缓存
        private readonly ConcurrentDictionary<long, byte[]> _preloadCache = new(); // 预加载缓存
        private long _currentPosition;
        private bool _isDisposed;
        
        // 激进预加载策略
        private CancellationTokenSource _preloadCancellation;
        private readonly int _maxCacheChunks = 8; // 最多缓存8个块（约80MB）
        private readonly int _aggressivePreloadCount = 4; // 激进预加载4个块
        
        // 性能优化
        private long _lastAccessPosition = -1;
        private bool _isSequentialAccess = true;
        private readonly object _accessPatternLock = new object();
        
        public OptimizedChunkedBufferMediaInput(Stream sourceStream, long estimatedBitrate = 0)
        {
            _sourceStream = sourceStream ?? throw new ArgumentNullException(nameof(sourceStream));
            _totalLength = sourceStream.Length;
            _currentPosition = 0;
            
            // 设置为可寻址
            CanSeek = true;
            
            // 计算优化的块大小
            if (estimatedBitrate > 0)
            {
                // 针对LibVLC的随机访问，使用较小的块（5秒）以提高响应速度
                _chunkSizeBytes = Math.Max((int)(estimatedBitrate * 5 / 8), 2 * 1024 * 1024); // 最小2MB
            }
            else
            {
                _chunkSizeBytes = 5 * 1024 * 1024; // 默认5MB块
            }
            
            _preloadCancellation = new CancellationTokenSource();
        }

        public override bool Open(out ulong size)
        {
            size = (ulong)_totalLength;
            
            // 立即预加载前几个块
            Task.Run(() => AggressivePreloadStart());
            
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
                    // 分析访问模式
                    AnalyzeAccessPattern(_currentPosition);
                    
                    // 快速获取数据
                    var data = GetDataFast(_currentPosition, (int)len);
                    if (data == null || data.Length == 0)
                        return 0;

                    // 复制数据
                    Marshal.Copy(data, 0, buf, data.Length);
                    _currentPosition += data.Length;
                    
                    // 触发智能预加载
                    TriggerSmartPreload();
                    
                    return data.Length;
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
                
                // 重置访问模式分析
                lock (_accessPatternLock)
                {
                    _isSequentialAccess = false;
                    _lastAccessPosition = _currentPosition;
                }
                
                // 立即预加载目标位置周围的数据
                Task.Run(() => PreloadAroundPosition(_currentPosition));
                
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
                
                _preloadCancellation?.Cancel();
                
                _preloadCancellation?.Dispose();
                _hotCache.Clear();
                _preloadCache.Clear();
                
                _sourceStream?.Dispose();
            }
            
            base.Dispose(disposing);
        }

        /// <summary>
        /// 快速获取数据 - 优先从缓存获取
        /// </summary>
        private byte[] GetDataFast(long position, int length)
        {
            var chunkStart = (position / _chunkSizeBytes) * _chunkSizeBytes;
            
            // 1. 优先从热点缓存获取
            if (_hotCache.TryGetValue(chunkStart, out var hotChunk))
            {
                return ExtractDataFromChunk(hotChunk, chunkStart, position, length);
            }
            
            // 2. 从预加载缓存获取并提升到热点缓存
            if (_preloadCache.TryRemove(chunkStart, out var preloadedChunk))
            {
                _hotCache.TryAdd(chunkStart, preloadedChunk);
                CleanupCache(); // 清理过期缓存
                return ExtractDataFromChunk(preloadedChunk, chunkStart, position, length);
            }
            
            // 3. 立即同步加载（阻塞，但必要时使用）
            var chunk = LoadChunkSync(chunkStart);
            if (chunk != null)
            {
                _hotCache.TryAdd(chunkStart, chunk);
                CleanupCache();
                return ExtractDataFromChunk(chunk, chunkStart, position, length);
            }
            
            return null;
        }

        /// <summary>
        /// 从块中提取指定范围的数据
        /// </summary>
        private byte[] ExtractDataFromChunk(byte[] chunk, long chunkStart, long position, int length)
        {
            var offsetInChunk = (int)(position - chunkStart);
            var availableInChunk = chunk.Length - offsetInChunk;
            
            if (availableInChunk <= 0)
                return new byte[0];
            
            var bytesToRead = Math.Min(length, availableInChunk);
            var result = new byte[bytesToRead];
            Array.Copy(chunk, offsetInChunk, result, 0, bytesToRead);
            
            return result;
        }

        /// <summary>
        /// 同步加载块
        /// </summary>
        private byte[] LoadChunkSync(long startPosition)
        {
            try
            {
                var chunkSize = Math.Min(_chunkSizeBytes, (int)(_totalLength - startPosition));
                if (chunkSize <= 0) return null;

                var chunk = new byte[chunkSize];
                
                lock (_sourceStream)
                {
                    _sourceStream.Position = startPosition;
                    var bytesRead = _sourceStream.Read(chunk, 0, chunkSize);
                    
                    if (bytesRead < chunkSize)
                    {
                        Array.Resize(ref chunk, bytesRead);
                    }
                }

                return chunk;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 分析访问模式
        /// </summary>
        private void AnalyzeAccessPattern(long currentPosition)
        {
            lock (_accessPatternLock)
            {
                if (_lastAccessPosition >= 0)
                {
                    var distance = Math.Abs(currentPosition - _lastAccessPosition);
                    _isSequentialAccess = distance <= _chunkSizeBytes * 2; // 如果跳跃距离小于2个块，认为是顺序访问
                }
                _lastAccessPosition = currentPosition;
            }
        }

        /// <summary>
        /// 智能预加载触发
        /// </summary>
        private void TriggerSmartPreload()
        {
            Task.Run(() =>
            {
                try
                {
                    var currentChunkStart = (_currentPosition / _chunkSizeBytes) * _chunkSizeBytes;
                    
                    if (_isSequentialAccess)
                    {
                        // 顺序访问：预加载后续块
                        for (int i = 1; i <= _aggressivePreloadCount; i++)
                        {
                            var nextChunkStart = currentChunkStart + (i * _chunkSizeBytes);
                            if (nextChunkStart < _totalLength)
                            {
                                PreloadChunkAsync(nextChunkStart);
                            }
                        }
                    }
                    else
                    {
                        // 随机访问：预加载周围的块
                        PreloadAroundPosition(_currentPosition);
                    }
                }
                catch
                {
                    // 预加载失败不影响主流程
                }
            }, _preloadCancellation.Token);
        }

        /// <summary>
        /// 激进的启动预加载
        /// </summary>
        private void AggressivePreloadStart()
        {
            try
            {
                // 预加载前4个块
                for (int i = 0; i < 4; i++)
                {
                    var chunkStart = i * _chunkSizeBytes;
                    if (chunkStart < _totalLength)
                    {
                        PreloadChunkAsync(chunkStart);
                    }
                }
            }
            catch
            {
                // 预加载失败不影响主流程
            }
        }

        /// <summary>
        /// 预加载指定位置周围的数据
        /// </summary>
        private void PreloadAroundPosition(long position)
        {
            try
            {
                var centerChunkStart = (position / _chunkSizeBytes) * _chunkSizeBytes;
                
                // 预加载中心块前后各2个块
                for (int i = -2; i <= 2; i++)
                {
                    var chunkStart = centerChunkStart + (i * _chunkSizeBytes);
                    if (chunkStart >= 0 && chunkStart < _totalLength)
                    {
                        PreloadChunkAsync(chunkStart);
                    }
                }
            }
            catch
            {
                // 预加载失败不影响主流程
            }
        }

        /// <summary>
        /// 异步预加载块
        /// </summary>
        private void PreloadChunkAsync(long startPosition)
        {
            if (_hotCache.ContainsKey(startPosition) || _preloadCache.ContainsKey(startPosition))
                return; // 已经缓存

            Task.Run(() =>
            {
                try
                {
                    var chunk = LoadChunkSync(startPosition);
                    if (chunk != null)
                    {
                        _preloadCache.TryAdd(startPosition, chunk);
                        CleanupCache();
                    }
                }
                catch
                {
                    // 预加载失败不影响主流程
                }
            }, _preloadCancellation.Token);
        }

        /// <summary>
        /// 清理过期缓存
        /// </summary>
        private void CleanupCache()
        {
            var totalCached = _hotCache.Count + _preloadCache.Count;
            if (totalCached <= _maxCacheChunks) return;

            try
            {
                // 清理距离当前位置最远的预加载缓存
                var currentChunkStart = (_currentPosition / _chunkSizeBytes) * _chunkSizeBytes;
                var toRemove = new List<long>();
                
                foreach (var kvp in _preloadCache)
                {
                    var distance = Math.Abs(kvp.Key - currentChunkStart);
                    if (distance > _chunkSizeBytes * 4) // 距离超过4个块的清理掉
                    {
                        toRemove.Add(kvp.Key);
                    }
                }
                
                foreach (var key in toRemove)
                {
                    _preloadCache.TryRemove(key, out _);
                }
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }
    }
}