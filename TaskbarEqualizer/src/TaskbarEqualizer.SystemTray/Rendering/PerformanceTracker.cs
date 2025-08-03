using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.SystemTray.Rendering
{
    /// <summary>
    /// High-performance tracking for icon rendering metrics and optimization.
    /// Provides real-time performance monitoring with minimal overhead.
    /// </summary>
    public sealed class PerformanceTracker : IDisposable
    {
        private readonly ConcurrentQueue<FrameMetric> _frameHistory = new();
        private readonly object _metricsLock = new();
        private readonly Timer _maintenanceTimer;
        
        private volatile bool _disposed;
        
        // Performance counters
        private long _totalFrames;
        private long _skippedFrames;
        private long _totalRenderTime; // in ticks
        private long _minRenderTime = long.MaxValue;
        private long _maxRenderTime = long.MinValue;
        
        // Rolling window metrics
        private readonly int _maxHistorySize = 1000;
        private int _currentHistorySize;
        
        // Performance thresholds (in milliseconds)
        private const double TargetFrameTime = 16.67; // 60 FPS target
        private const double PerformanceThreshold = 33.33; // 30 FPS threshold
        
        // Advanced metrics
        private long _p95RenderTime = 0;
        private long _adaptiveQualityTriggers = 0;
        private readonly Dictionary<string, double> _renderingBreakdown = new();
        
        // GDI+ object tracking
        private int _gdiObjectCount = 0;
        private long _memoryUsage = 0;
        
        // CPU usage tracking
        private readonly PerformanceCounter? _cpuCounter;
        private double _lastCpuUsage = 0.0;

        /// <summary>
        /// Initializes a new instance of the PerformanceTracker.
        /// </summary>
        public PerformanceTracker()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue(); // Initialize
            }
            catch
            {
                // CPU counter initialization failed - continue without it
                _cpuCounter = null;
            }

            // Setup maintenance timer to run every 5 seconds
            _maintenanceTimer = new Timer(PerformMaintenance, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Records a completed frame with performance metrics.
        /// </summary>
        /// <param name="renderTime">Time taken to render the frame.</param>
        /// <param name="wasSkipped">Whether the frame was skipped for optimization.</param>
        public void RecordFrame(TimeSpan renderTime, bool wasSkipped = false)
        {
            if (_disposed)
                return;

            var renderTicks = renderTime.Ticks;
            var renderMs = renderTime.TotalMilliseconds;

            // Update counters
            Interlocked.Increment(ref _totalFrames);
            
            if (wasSkipped)
            {
                Interlocked.Increment(ref _skippedFrames);
            }
            else
            {
                Interlocked.Add(ref _totalRenderTime, renderTicks);
                
                // Update min/max render times
                UpdateMinMax(renderTicks);
            }

            // Add to frame history for detailed analysis
            var frameMetric = new FrameMetric
            {
                Timestamp = DateTime.UtcNow,
                RenderTime = renderTime,
                WasSkipped = wasSkipped,
                MemoryUsage = GC.GetTotalMemory(false)
            };

            _frameHistory.Enqueue(frameMetric);
            Interlocked.Increment(ref _currentHistorySize);

            // Trim history if needed
            if (_currentHistorySize > _maxHistorySize)
            {
                if (_frameHistory.TryDequeue(out _))
                {
                    Interlocked.Decrement(ref _currentHistorySize);
                }
            }
        }

        /// <summary>
        /// Records a rendering operation breakdown for detailed analysis.
        /// </summary>
        /// <param name="operation">Name of the operation.</param>
        /// <param name="duration">Time taken for the operation.</param>
        public void RecordOperationTime(string operation, TimeSpan duration)
        {
            if (_disposed || string.IsNullOrEmpty(operation))
                return;

            lock (_metricsLock)
            {
                if (_renderingBreakdown.ContainsKey(operation))
                {
                    // Running average
                    _renderingBreakdown[operation] = (_renderingBreakdown[operation] + duration.TotalMilliseconds) / 2.0;
                }
                else
                {
                    _renderingBreakdown[operation] = duration.TotalMilliseconds;
                }
            }
        }

        /// <summary>
        /// Records when adaptive quality was triggered due to performance constraints.
        /// </summary>
        public void RecordAdaptiveQualityTrigger()
        {
            if (_disposed)
                return;

            Interlocked.Increment(ref _adaptiveQualityTriggers);
        }

        /// <summary>
        /// Updates GDI+ object count for resource monitoring.
        /// </summary>
        /// <param name="count">Current GDI+ object count.</param>
        public void UpdateGdiObjectCount(int count)
        {
            if (_disposed)
                return;

            Interlocked.Exchange(ref _gdiObjectCount, count);
        }

        /// <summary>
        /// Updates memory usage estimate.
        /// </summary>
        /// <param name="bytes">Memory usage in bytes.</param>
        public void UpdateMemoryUsage(long bytes)
        {
            if (_disposed)
                return;

            Interlocked.Exchange(ref _memoryUsage, bytes);
        }

        /// <summary>
        /// Checks if the system is currently performance-constrained.
        /// </summary>
        /// <returns>True if performance is below acceptable thresholds.</returns>
        public bool IsPerformanceConstrained()
        {
            if (_disposed || _totalFrames == 0)
                return false;

            var averageRenderTime = GetAverageRenderTime();
            
            // Consider performance constrained if:
            // 1. Average render time exceeds threshold
            // 2. Skip rate is high
            // 3. CPU usage is high
            
            var skipRate = (double)_skippedFrames / _totalFrames;
            
            return averageRenderTime > PerformanceThreshold || 
                   skipRate > 0.3 || 
                   _lastCpuUsage > 80.0;
        }

        /// <summary>
        /// Gets current rendering metrics.
        /// </summary>
        /// <returns>Current performance metrics.</returns>
        public RenderingMetrics GetCurrentMetrics()
        {
            if (_disposed)
                return new RenderingMetrics();

            var totalFrames = _totalFrames;
            var skippedFrames = _skippedFrames;
            var averageRenderTime = GetAverageRenderTime();
            
            var frameRate = 0.0;
            if (averageRenderTime > 0)
            {
                frameRate = 1000.0 / averageRenderTime; // Convert ms to FPS
            }

            return new RenderingMetrics
            {
                AverageRenderTime = averageRenderTime,
                FrameRate = frameRate,
                FrameCount = totalFrames,
                SkippedFrames = skippedFrames,
                MemoryUsage = _memoryUsage,
                CpuUsage = _lastCpuUsage
            };
        }

        /// <summary>
        /// Gets detailed rendering metrics for analysis.
        /// </summary>
        /// <returns>Comprehensive performance metrics.</returns>
        public DetailedRenderingMetrics GetDetailedMetrics()
        {
            if (_disposed)
                return new DetailedRenderingMetrics();

            var basicMetrics = GetCurrentMetrics();
            var p95Time = CalculateP95RenderTime();
            var maxTime = TimeSpan.FromTicks(_maxRenderTime).TotalMilliseconds;
            var cacheHitRate = CalculateCacheHitRate();

            var detailedMetrics = new DetailedRenderingMetrics
            {
                AverageRenderTime = basicMetrics.AverageRenderTime,
                FrameRate = basicMetrics.FrameRate,
                FrameCount = basicMetrics.FrameCount,
                SkippedFrames = basicMetrics.SkippedFrames,
                MemoryUsage = basicMetrics.MemoryUsage,
                CpuUsage = basicMetrics.CpuUsage,
                P95RenderTime = p95Time,
                MaxRenderTime = maxTime,
                GdiObjectCount = _gdiObjectCount,
                CacheHitRate = cacheHitRate,
                AdaptiveQualityTriggers = _adaptiveQualityTriggers
            };

            // Copy rendering breakdown
            lock (_metricsLock)
            {
                foreach (var kvp in _renderingBreakdown)
                {
                    detailedMetrics.RenderingBreakdown[kvp.Key] = kvp.Value;
                }
            }

            return detailedMetrics;
        }

        /// <summary>
        /// Resets all performance counters.
        /// </summary>
        public void Reset()
        {
            if (_disposed)
                return;

            lock (_metricsLock)
            {
                _totalFrames = 0;
                _skippedFrames = 0;
                _totalRenderTime = 0;
                _minRenderTime = long.MaxValue;
                _maxRenderTime = long.MinValue;
                _p95RenderTime = 0;
                _adaptiveQualityTriggers = 0;
                _gdiObjectCount = 0;
                _memoryUsage = 0;
                
                _renderingBreakdown.Clear();
                
                // Clear frame history
                while (_frameHistory.TryDequeue(out _)) { }
                _currentHistorySize = 0;
            }
        }

        private void UpdateMinMax(long renderTicks)
        {
            // Update minimum
            long currentMin = _minRenderTime;
            while (renderTicks < currentMin)
            {
                long originalMin = Interlocked.CompareExchange(ref _minRenderTime, renderTicks, currentMin);
                if (originalMin == currentMin)
                    break;
                currentMin = originalMin;
            }

            // Update maximum
            long currentMax = _maxRenderTime;
            while (renderTicks > currentMax)
            {
                long originalMax = Interlocked.CompareExchange(ref _maxRenderTime, renderTicks, currentMax);
                if (originalMax == currentMax)
                    break;
                currentMax = originalMax;
            }
        }

        private double GetAverageRenderTime()
        {
            var totalFrames = _totalFrames;
            var skippedFrames = _skippedFrames;
            var renderedFrames = totalFrames - skippedFrames;
            
            if (renderedFrames == 0)
                return 0.0;

            var totalTicks = _totalRenderTime;
            var averageTicks = totalTicks / (double)renderedFrames;
            
            return TimeSpan.FromTicks((long)averageTicks).TotalMilliseconds;
        }

        private double CalculateP95RenderTime()
        {
            var frames = _frameHistory.ToArray()
                .Where(f => !f.WasSkipped)
                .Select(f => f.RenderTime.TotalMilliseconds)
                .OrderBy(t => t)
                .ToArray();

            if (frames.Length == 0)
                return 0.0;

            var p95Index = (int)(frames.Length * 0.95);
            return frames[Math.Min(p95Index, frames.Length - 1)];
        }

        private double CalculateCacheHitRate()
        {
            // This would typically be provided by the render cache
            // For now, estimate based on skip rate (higher skip rate often means better caching)
            if (_totalFrames == 0)
                return 0.0;

            return Math.Min(1.0, (double)_skippedFrames / _totalFrames * 2.0);
        }

        private void PerformMaintenance(object? state)
        {
            if (_disposed)
                return;

            try
            {
                // Update CPU usage
                if (_cpuCounter != null)
                {
                    _lastCpuUsage = _cpuCounter.NextValue();
                }

                // Clean up old frame history (older than 1 minute)
                var cutoffTime = DateTime.UtcNow.AddMinutes(-1);
                while (_frameHistory.TryPeek(out var oldFrame) && oldFrame.Timestamp < cutoffTime)
                {
                    if (_frameHistory.TryDequeue(out _))
                    {
                        Interlocked.Decrement(ref _currentHistorySize);
                    }
                }

                // Update P95 render time
                _p95RenderTime = (long)(CalculateP95RenderTime() * TimeSpan.TicksPerMillisecond);
            }
            catch
            {
                // Ignore maintenance errors
            }
        }

        /// <summary>
        /// Disposes the performance tracker and releases resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _maintenanceTimer?.Dispose();
                _cpuCounter?.Dispose();
            }
        }

        /// <summary>
        /// Individual frame performance metric.
        /// </summary>
        private class FrameMetric
        {
            public DateTime Timestamp { get; set; }
            public TimeSpan RenderTime { get; set; }
            public bool WasSkipped { get; set; }
            public long MemoryUsage { get; set; }
        }
    }
}