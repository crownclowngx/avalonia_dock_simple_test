using System.Collections.Concurrent;

namespace MySmallTools.Business.SecretVideoPlayer;

/// <summary>
/// 多级视频缓冲管理器 - 使用LRU策略和智能预读取
/// </summary>
public class MultiLevelVideoBuffer : IDisposable
{
    private readonly ConcurrentDictionary<long, BufferBlock> _cache;
    private readonly LinkedList<long> _lruList;
    private readonly object _lruLock = new();
    private readonly int _maxCacheBlocks;
    private readonly Timer _cleanupTimer;
    
    private long _hitCount;
    private long _missCount;
    private long _totalRequests;
    private bool _disposed;
    
    public int BlockSize { get; }
    public int MaxCacheBlocks => _maxCacheBlocks;
    
    public MultiLevelVideoBuffer(int blockSize = 1024 * 1024, int maxCacheBlocks = 10)
    {
        if (blockSize <= 0) throw new ArgumentException("Block size must be positive", nameof(blockSize));
        if (maxCacheBlocks <= 0) throw new ArgumentException("Max cache blocks must be positive", nameof(maxCacheBlocks));
        
        BlockSize = blockSize;
        _maxCacheBlocks = maxCacheBlocks;
        _cache = new ConcurrentDictionary<long, BufferBlock>();
        _lruList = new LinkedList<long>();
        
        // 定期清理过期的缓存块
        _cleanupTimer = new Timer(CleanupExpiredBlocks, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }
    
    /// <summary>
    /// 获取数据块
    /// </summary>
    public byte[]? GetBlock(long blockIndex)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiLevelVideoBuffer));
        
        Interlocked.Increment(ref _totalRequests);
        
        if (_cache.TryGetValue(blockIndex, out var block))
        {
            // 缓存命中
            Interlocked.Increment(ref _hitCount);
            block.LastAccessTime = DateTime.UtcNow;
            block.AccessCount++;
            
            // 更新LRU顺序
            UpdateLruOrder(blockIndex);
            
            return block.Data;
        }
        
        // 缓存未命中
        Interlocked.Increment(ref _missCount);
        return null;
    }
    
    /// <summary>
    /// 存储数据块
    /// </summary>
    public void PutBlock(long blockIndex, byte[] data)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiLevelVideoBuffer));
        if (data == null) throw new ArgumentNullException(nameof(data));
        
        var block = new BufferBlock
        {
            Index = blockIndex,
            Data = data,
            CreationTime = DateTime.UtcNow,
            LastAccessTime = DateTime.UtcNow,
            AccessCount = 1
        };
        
        // 添加到缓存
        _cache.AddOrUpdate(blockIndex, block, (key, existing) =>
        {
            existing.Data = data;
            existing.LastAccessTime = DateTime.UtcNow;
            existing.AccessCount++;
            return existing;
        });
        
        // 更新LRU顺序
        UpdateLruOrder(blockIndex);
        
        // 检查是否需要清理缓存
        if (_cache.Count > _maxCacheBlocks)
        {
            EvictLeastRecentlyUsed();
        }
    }
    
    /// <summary>
    /// 检查是否存在指定的数据块
    /// </summary>
    public bool HasBlock(long blockIndex)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiLevelVideoBuffer));
        return _cache.ContainsKey(blockIndex);
    }
    
    /// <summary>
    /// 预加载数据块范围
    /// </summary>
    public void PreloadRange(long startBlock, long endBlock, Func<long, byte[]?> dataLoader)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiLevelVideoBuffer));
        if (dataLoader == null) throw new ArgumentNullException(nameof(dataLoader));
        
        var tasks = new List<Task>();
        
        for (var blockIndex = startBlock; blockIndex <= endBlock; blockIndex++)
        {
            if (!HasBlock(blockIndex))
            {
                var index = blockIndex; // 捕获循环变量
                tasks.Add(Task.Run(() =>
                {
                    var data = dataLoader(index);
                    if (data != null)
                    {
                        PutBlock(index, data);
                    }
                }));
            }
        }
        
        // 限制并发任务数量
        if (tasks.Count > 0)
        {
            Task.WhenAll(tasks).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    // 记录预加载错误，但不抛出异常
                    Console.WriteLine($"Preload error: {t.Exception?.GetBaseException().Message}");
                }
            });
        }
    }
    
    /// <summary>
    /// 智能预读取 - 基于访问模式预测下一个需要的块
    /// </summary>
    public void SmartPreload(long currentBlock, Func<long, byte[]?> dataLoader, int preloadCount = 3)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiLevelVideoBuffer));
        
        // 预读取后续的块
        var endBlock = currentBlock + preloadCount;
        PreloadRange(currentBlock + 1, endBlock, dataLoader);
        
        // 基于访问模式的智能预测
        var accessPattern = AnalyzeAccessPattern(currentBlock);
        if (accessPattern.IsSequential)
        {
            // 顺序访问模式，预读取更多块
            PreloadRange(endBlock + 1, endBlock + preloadCount, dataLoader);
        }
        else if (accessPattern.IsRandom)
        {
            // 随机访问模式，预读取相邻块
            if (currentBlock > 0)
            {
                PreloadRange(Math.Max(0, currentBlock - 2), currentBlock - 1, dataLoader);
            }
        }
    }
    
    /// <summary>
    /// 分析访问模式
    /// </summary>
    private AccessPattern AnalyzeAccessPattern(long currentBlock)
    {
        var recentBlocks = new List<long>();
        
        lock (_lruLock)
        {
            // 获取最近访问的块
            var count = 0;
            foreach (var blockIndex in _lruList)
            {
                recentBlocks.Add(blockIndex);
                if (++count >= 10) break; // 分析最近10个访问
            }
        }
        
        if (recentBlocks.Count < 3)
        {
            return new AccessPattern { IsSequential = false, IsRandom = false };
        }
        
        // 检查是否为顺序访问
        var isSequential = true;
        for (int i = 1; i < recentBlocks.Count; i++)
        {
            if (Math.Abs(recentBlocks[i] - recentBlocks[i - 1]) > 2)
            {
                isSequential = false;
                break;
            }
        }
        
        // 检查是否为随机访问
        var differences = new List<long>();
        for (int i = 1; i < recentBlocks.Count; i++)
        {
            differences.Add(Math.Abs(recentBlocks[i] - recentBlocks[i - 1]));
        }
        
        var avgDifference = differences.Average();
        var isRandom = avgDifference > 10; // 平均跳跃超过10个块认为是随机访问
        
        return new AccessPattern
        {
            IsSequential = isSequential,
            IsRandom = isRandom,
            AverageJumpDistance = avgDifference
        };
    }
    
    /// <summary>
    /// 更新LRU顺序
    /// </summary>
    private void UpdateLruOrder(long blockIndex)
    {
        lock (_lruLock)
        {
            // 移除现有节点
            _lruList.Remove(blockIndex);
            
            // 添加到头部（最近使用）
            _lruList.AddFirst(blockIndex);
        }
    }
    
    /// <summary>
    /// 淘汰最近最少使用的块
    /// </summary>
    private void EvictLeastRecentlyUsed()
    {
        var blocksToEvict = new List<long>();
        
        lock (_lruLock)
        {
            // 计算需要淘汰的块数量
            var evictCount = _cache.Count - _maxCacheBlocks + 1;
            
            // 从LRU列表尾部开始淘汰
            var current = _lruList.Last;
            while (current != null && evictCount > 0)
            {
                blocksToEvict.Add(current.Value);
                var prev = current.Previous;
                _lruList.Remove(current);
                current = prev;
                evictCount--;
            }
        }
        
        // 从缓存中移除
        foreach (var blockIndex in blocksToEvict)
        {
            _cache.TryRemove(blockIndex, out _);
        }
    }
    
    /// <summary>
    /// 清理过期的缓存块
    /// </summary>
    private void CleanupExpiredBlocks(object? state)
    {
        if (_disposed) return;
        
        var expiredBlocks = new List<long>();
        var cutoffTime = DateTime.UtcNow.AddMinutes(-30); // 30分钟未访问的块
        
        foreach (var kvp in _cache)
        {
            if (kvp.Value.LastAccessTime < cutoffTime && kvp.Value.AccessCount < 3)
            {
                expiredBlocks.Add(kvp.Key);
            }
        }
        
        foreach (var blockIndex in expiredBlocks)
        {
            _cache.TryRemove(blockIndex, out _);
            
            lock (_lruLock)
            {
                _lruList.Remove(blockIndex);
            }
        }
    }
    
    /// <summary>
    /// 清空缓存
    /// </summary>
    public void Clear()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MultiLevelVideoBuffer));
        
        _cache.Clear();
        
        lock (_lruLock)
        {
            _lruList.Clear();
        }
        
        Interlocked.Exchange(ref _hitCount, 0);
        Interlocked.Exchange(ref _missCount, 0);
        Interlocked.Exchange(ref _totalRequests, 0);
    }
    
    /// <summary>
    /// 获取缓存统计信息
    /// </summary>
    public BufferStatistics GetStatistics()
    {
        var totalRequests = Interlocked.Read(ref _totalRequests);
        var hitCount = Interlocked.Read(ref _hitCount);
        var missCount = Interlocked.Read(ref _missCount);
        
        return new BufferStatistics
        {
            TotalRequests = totalRequests,
            HitCount = hitCount,
            MissCount = missCount,
            HitRate = totalRequests > 0 ? (double)hitCount / totalRequests : 0,
            CachedBlocks = _cache.Count,
            MaxCacheBlocks = _maxCacheBlocks,
            BlockSize = BlockSize,
            TotalMemoryUsage = _cache.Values.Sum(b => b.Data.Length)
        };
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _cleanupTimer?.Dispose();
            Clear();
            _disposed = true;
        }
    }
}

/// <summary>
/// 缓冲区数据块
/// </summary>
internal class BufferBlock
{
    public long Index { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public DateTime CreationTime { get; set; }
    public DateTime LastAccessTime { get; set; }
    public int AccessCount { get; set; }
}

/// <summary>
/// 访问模式分析结果
/// </summary>
internal class AccessPattern
{
    public bool IsSequential { get; set; }
    public bool IsRandom { get; set; }
    public double AverageJumpDistance { get; set; }
}

/// <summary>
/// 缓冲区统计信息
/// </summary>
public class BufferStatistics
{
    public long TotalRequests { get; set; }
    public long HitCount { get; set; }
    public long MissCount { get; set; }
    public double HitRate { get; set; }
    public int CachedBlocks { get; set; }
    public int MaxCacheBlocks { get; set; }
    public int BlockSize { get; set; }
    public long TotalMemoryUsage { get; set; }
}