using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.SystemTray.Rendering
{
    /// <summary>
    /// High-performance icon cache for rendering optimization.
    /// Implements LRU eviction and size-based memory management.
    /// </summary>
    public sealed class RenderCache : IDisposable
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
        private readonly ConcurrentQueue<string> _accessOrder = new();
        private readonly object _evictionLock = new();
        
        private volatile bool _disposed;
        private volatile bool _isInitialized;
        
        // Cache configuration
        private int _maxCacheSize;
        private IconSize _iconSize;
        private RenderQuality _quality;
        
        // Cache statistics
        private long _totalRequests;
        private long _cacheHits;
        private long _cacheMisses;
        private long _evictions;
        private int _currentSize;
        
        // Last rendered icon for fallback
        private Icon? _lastRenderedIcon;
        private DateTime _lastRenderedTime = DateTime.MinValue;

        /// <summary>
        /// Initializes a new instance of the RenderCache.
        /// </summary>
        /// <param name="maxCacheSize">Maximum number of cached icons.</param>
        public RenderCache(int maxCacheSize = 100)
        {
            _maxCacheSize = Math.Max(1, maxCacheSize);
        }

        /// <summary>
        /// Gets cache performance statistics.
        /// </summary>
        public CacheStatistics Statistics => new()
        {
            TotalRequests = _totalRequests,
            CacheHits = _cacheHits,
            CacheMisses = _cacheMisses,
            Evictions = _evictions,
            CurrentSize = _currentSize,
            MaxSize = _maxCacheSize,
            HitRate = _totalRequests > 0 ? (double)_cacheHits / _totalRequests : 0.0,
            MemoryUsageEstimate = EstimateMemoryUsage()
        };

        /// <summary>
        /// Initializes the cache with the specified parameters.
        /// </summary>
        /// <param name="iconSize">Target icon size for cache optimization.</param>
        /// <param name="quality">Rendering quality level.</param>
        public void Initialize(IconSize iconSize, RenderQuality quality)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RenderCache));

            if (_isInitialized)
                return;

            _iconSize = iconSize;
            _quality = quality;
            _isInitialized = true;
        }

        /// <summary>
        /// Gets a cached icon by key.
        /// </summary>
        /// <param name="cacheKey">Unique cache key for the icon.</param>
        /// <returns>Cached icon if found, null otherwise.</returns>
        public Icon? GetCachedIcon(string cacheKey)
        {
            if (_disposed || string.IsNullOrEmpty(cacheKey))
                return null;

            Interlocked.Increment(ref _totalRequests);

            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                // Update access time and order
                entry.LastAccessed = DateTime.UtcNow;
                _accessOrder.Enqueue(cacheKey);
                
                Interlocked.Increment(ref _cacheHits);
                
                // Clone the icon to prevent disposal issues
                return entry.Icon != null ? CloneIcon(entry.Icon) : null;
            }

            Interlocked.Increment(ref _cacheMisses);
            return null;
        }

        /// <summary>
        /// Caches an icon with the specified key.
        /// </summary>
        /// <param name="cacheKey">Unique cache key for the icon.</param>
        /// <param name="icon">Icon to cache.</param>
        public void CacheIcon(string cacheKey, Icon icon)
        {
            if (_disposed || string.IsNullOrEmpty(cacheKey) || icon == null)
                return;

            try
            {
                // Create cache entry
                var entry = new CacheEntry
                {
                    Icon = CloneIcon(icon),
                    CacheKey = cacheKey,
                    CreatedAt = DateTime.UtcNow,
                    LastAccessed = DateTime.UtcNow,
                    Size = EstimateIconSize(icon)
                };

                // Add to cache
                if (_cache.TryAdd(cacheKey, entry))
                {
                    Interlocked.Increment(ref _currentSize);
                    _accessOrder.Enqueue(cacheKey);
                    
                    // Update last rendered icon
                    _lastRenderedIcon?.Dispose();
                    _lastRenderedIcon = icon != null ? CloneIcon(icon) : null;
                    _lastRenderedTime = DateTime.UtcNow;

                    // Check if eviction is needed
                    if (_currentSize > _maxCacheSize)
                    {
                        _ = Task.Run(EvictLeastRecentlyUsed);
                    }
                }
                else
                {
                    // Failed to add, dispose the cloned icon
                    entry.Icon?.Dispose();
                }
            }
            catch (Exception)
            {
                // Silently fail - caching is an optimization, not critical
            }
        }

        /// <summary>
        /// Gets the last rendered icon for fallback scenarios.
        /// </summary>
        /// <returns>Last rendered icon if available, null otherwise.</returns>
        public Icon? GetLastRenderedIcon()
        {
            if (_disposed || _lastRenderedIcon == null)
                return null;

            // Only return if it's recent (within last 5 seconds)
            if (DateTime.UtcNow - _lastRenderedTime > TimeSpan.FromSeconds(5))
                return null;

            return CloneIcon(_lastRenderedIcon);
        }

        /// <summary>
        /// Checks if a cache key exists.
        /// </summary>
        /// <param name="cacheKey">Cache key to check.</param>
        /// <returns>True if key exists in cache.</returns>
        public bool ContainsKey(string cacheKey)
        {
            if (_disposed || string.IsNullOrEmpty(cacheKey))
                return false;

            return _cache.ContainsKey(cacheKey);
        }

        /// <summary>
        /// Removes a specific entry from the cache.
        /// </summary>
        /// <param name="cacheKey">Cache key to remove.</param>
        /// <returns>True if entry was removed.</returns>
        public bool RemoveEntry(string cacheKey)
        {
            if (_disposed || string.IsNullOrEmpty(cacheKey))
                return false;

            if (_cache.TryRemove(cacheKey, out var entry))
            {
                entry.Icon?.Dispose();
                Interlocked.Decrement(ref _currentSize);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clears all cached entries.
        /// </summary>
        public void Clear()
        {
            if (_disposed)
                return;

            lock (_evictionLock)
            {
                foreach (var entry in _cache.Values)
                {
                    entry.Icon?.Dispose();
                }

                _cache.Clear();
                
                // Clear access order queue
                while (_accessOrder.TryDequeue(out _)) { }

                // Reset counters
                _currentSize = 0;
                _totalRequests = 0;
                _cacheHits = 0;
                _cacheMisses = 0;
                _evictions = 0;
            }
        }

        /// <summary>
        /// Updates the maximum cache size.
        /// </summary>
        /// <param name="newMaxSize">New maximum cache size.</param>
        public void UpdateMaxSize(int newMaxSize)
        {
            if (_disposed)
                return;

            _maxCacheSize = Math.Max(1, newMaxSize);

            // Trigger eviction if current size exceeds new limit
            if (_currentSize > _maxCacheSize)
            {
                _ = Task.Run(EvictLeastRecentlyUsed);
            }
        }

        /// <summary>
        /// Performs maintenance operations like removing expired entries.
        /// </summary>
        public void PerformMaintenance()
        {
            if (_disposed)
                return;

            var cutoffTime = DateTime.UtcNow.AddMinutes(-5); // Remove entries older than 5 minutes

            var keysToRemove = new List<string>();

            foreach (var kvp in _cache)
            {
                if (kvp.Value.LastAccessed < cutoffTime)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                RemoveEntry(key);
            }
        }

        private void EvictLeastRecentlyUsed()
        {
            if (_disposed)
                return;

            lock (_evictionLock)
            {
                var targetSize = (int)(_maxCacheSize * 0.8); // Evict to 80% capacity
                var keysToRemove = new List<string>();

                // Build list of keys ordered by last access time
                var orderedEntries = _cache.ToArray()
                    .OrderBy(kvp => kvp.Value.LastAccessed)
                    .ToArray();

                int removeCount = Math.Max(0, _currentSize - targetSize);
                
                for (int i = 0; i < Math.Min(removeCount, orderedEntries.Length); i++)
                {
                    keysToRemove.Add(orderedEntries[i].Key);
                }

                // Remove the selected entries
                foreach (var key in keysToRemove)
                {
                    if (_cache.TryRemove(key, out var entry))
                    {
                        entry.Icon?.Dispose();
                        Interlocked.Decrement(ref _currentSize);
                        Interlocked.Increment(ref _evictions);
                    }
                }
            }
        }

        private Icon CloneIcon(Icon original)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));

            // Create a copy of the icon to prevent disposal issues
            return new Icon(original, original.Size);
        }

        private int EstimateIconSize(Icon icon)
        {
            if (icon == null)
                return 0;

            // Rough estimation: 4 bytes per pixel (ARGB) + overhead
            int pixelCount = icon.Width * icon.Height;
            return pixelCount * 4 + 256; // 256 bytes overhead
        }

        private long EstimateMemoryUsage()
        {
            long totalSize = 0;

            foreach (var entry in _cache.Values)
            {
                totalSize += entry.Size;
            }

            return totalSize;
        }

        /// <summary>
        /// Disposes the cache and all cached icons.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Clear();
                _lastRenderedIcon?.Dispose();
                _lastRenderedIcon = null;
            }
        }

        /// <summary>
        /// Cache entry containing icon and metadata.
        /// </summary>
        private class CacheEntry
        {
            public Icon? Icon { get; set; }
            public string CacheKey { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime LastAccessed { get; set; }
            public int Size { get; set; }
        }
    }

    /// <summary>
    /// Cache performance statistics.
    /// </summary>
    public class CacheStatistics
    {
        /// <summary>
        /// Total number of cache requests.
        /// </summary>
        public long TotalRequests { get; set; }

        /// <summary>
        /// Number of cache hits.
        /// </summary>
        public long CacheHits { get; set; }

        /// <summary>
        /// Number of cache misses.
        /// </summary>
        public long CacheMisses { get; set; }

        /// <summary>
        /// Number of cache evictions.
        /// </summary>
        public long Evictions { get; set; }

        /// <summary>
        /// Current number of cached items.
        /// </summary>
        public int CurrentSize { get; set; }

        /// <summary>
        /// Maximum cache size.
        /// </summary>
        public int MaxSize { get; set; }

        /// <summary>
        /// Cache hit rate (0.0-1.0).
        /// </summary>
        public double HitRate { get; set; }

        /// <summary>
        /// Estimated memory usage in bytes.
        /// </summary>
        public long MemoryUsageEstimate { get; set; }
    }
}