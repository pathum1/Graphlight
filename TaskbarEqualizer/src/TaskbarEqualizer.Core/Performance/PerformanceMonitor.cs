using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Core.Interfaces;

namespace TaskbarEqualizer.Core.Performance
{
    /// <summary>
    /// High-performance monitoring system for real-time audio processing applications.
    /// Tracks CPU usage, memory consumption, latency metrics, and performance thresholds.
    /// </summary>
    public sealed class PerformanceMonitor : IPerformanceMonitor
    {
        private readonly ILogger<PerformanceMonitor> _logger;
        private readonly PerformanceCounter _cpuCounter;
        private readonly Process _currentProcess;
        
        private PerformanceMetrics _currentMetrics;
        private PerformanceThresholds _thresholds;
        private readonly List<PerformanceMetrics> _historicalMetrics;
        private readonly Dictionary<string, TimingStatistics> _timingStats;
        private readonly Dictionary<string, long> _counters;
        private readonly Dictionary<string, double> _gauges;
        
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _monitoringTask;
        private volatile bool _isMonitoring;
        private volatile bool _disposed;
        
        private readonly object _lockObject = new object();
        private readonly Timer _gcCollectionTimer;
        private int _previousGcCollections;
        private long _startTime;

        /// <summary>
        /// Initializes a new instance of the PerformanceMonitor.
        /// </summary>
        /// <param name="logger">Logger for diagnostic information.</param>
        public PerformanceMonitor(ILogger<PerformanceMonitor> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _currentProcess = Process.GetCurrentProcess();
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            
            _currentMetrics = new PerformanceMetrics { Timestamp = DateTime.UtcNow };
            _thresholds = new PerformanceThresholds();
            _historicalMetrics = new List<PerformanceMetrics>();
            _timingStats = new Dictionary<string, TimingStatistics>();
            _counters = new Dictionary<string, long>();
            _gauges = new Dictionary<string, double>();
            
            _previousGcCollections = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
            _startTime = Environment.TickCount64;
            
            // Initialize GC collection monitoring timer
            _gcCollectionTimer = new Timer(UpdateGcMetrics, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            
            _logger.LogDebug("PerformanceMonitor initialized");
        }

        #region Events

        /// <inheritdoc />
        public event EventHandler<PerformanceMetricsEventArgs>? MetricsUpdated;

        /// <inheritdoc />
        public event EventHandler<PerformanceThresholdEventArgs>? ThresholdExceeded;

        #endregion

        #region Properties

        /// <inheritdoc />
        public PerformanceMetrics CurrentMetrics
        {
            get
            {
                lock (_lockObject)
                {
                    return _currentMetrics;
                }
            }
        }

        /// <inheritdoc />
        public bool IsMonitoring => _isMonitoring;

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public async Task StartMonitoringAsync(TimeSpan updateInterval, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PerformanceMonitor));

            if (_isMonitoring)
            {
                _logger.LogWarning("Performance monitoring is already running");
                return;
            }

            _logger.LogInformation("Starting performance monitoring with {UpdateInterval}ms interval", updateInterval.TotalMilliseconds);

            try
            {
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _monitoringTask = Task.Run(() => MonitoringLoop(updateInterval, _cancellationTokenSource.Token), _cancellationTokenSource.Token);
                
                _isMonitoring = true;
                _startTime = Environment.TickCount64;
                
                _logger.LogInformation("Performance monitoring started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start performance monitoring");
                throw;
            }

            await Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task StopMonitoringAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return;

            if (!_isMonitoring)
            {
                _logger.LogDebug("Performance monitoring is not running");
                return;
            }

            _logger.LogInformation("Stopping performance monitoring");

            try
            {
                _cancellationTokenSource?.Cancel();
                _isMonitoring = false;

                if (_monitoringTask != null)
                {
                    await _monitoringTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping performance monitoring");
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _monitoringTask = null;
            }

            _logger.LogInformation("Performance monitoring stopped");
        }

        /// <inheritdoc />
        public void RecordTiming(string operationName, TimeSpan duration)
        {
            if (_disposed || string.IsNullOrEmpty(operationName))
                return;

            lock (_lockObject)
            {
                if (!_timingStats.TryGetValue(operationName, out TimingStatistics? stats))
                {
                    stats = new TimingStatistics { OperationName = operationName };
                    _timingStats[operationName] = stats;
                }

                stats.AddSample(duration.TotalMilliseconds);
            }
        }

        /// <inheritdoc />
        public void RecordCounter(string counterName, long increment = 1)
        {
            if (_disposed || string.IsNullOrEmpty(counterName))
                return;

            lock (_lockObject)
            {
                _counters.TryGetValue(counterName, out long currentValue);
                _counters[counterName] = currentValue + increment;
            }
        }

        /// <inheritdoc />
        public void RecordGauge(string gaugeName, double value)
        {
            if (_disposed || string.IsNullOrEmpty(gaugeName))
                return;

            lock (_lockObject)
            {
                _gauges[gaugeName] = value;
            }
        }

        /// <inheritdoc />
        public void RecordAudioLatency(TimeSpan captureToProcessingLatency, TimeSpan processingLatency, TimeSpan totalLatency)
        {
            if (_disposed)
                return;

            lock (_lockObject)
            {
                if (_currentMetrics.AudioLatency.SampleCount == 0)
                {
                    _currentMetrics.AudioLatency.AverageCaptureToProcessingMs = captureToProcessingLatency.TotalMilliseconds;
                    _currentMetrics.AudioLatency.AverageProcessingMs = processingLatency.TotalMilliseconds;
                    _currentMetrics.AudioLatency.AverageTotalLatencyMs = totalLatency.TotalMilliseconds;
                    _currentMetrics.AudioLatency.P95TotalLatencyMs = totalLatency.TotalMilliseconds;
                    _currentMetrics.AudioLatency.MaxLatencyMs = totalLatency.TotalMilliseconds;
                    _currentMetrics.AudioLatency.SampleCount = 1;
                }
                else
                {
                    long count = _currentMetrics.AudioLatency.SampleCount;
                    
                    // Update running averages
                    _currentMetrics.AudioLatency.AverageCaptureToProcessingMs = 
                        (_currentMetrics.AudioLatency.AverageCaptureToProcessingMs * count + captureToProcessingLatency.TotalMilliseconds) / (count + 1);
                    
                    _currentMetrics.AudioLatency.AverageProcessingMs = 
                        (_currentMetrics.AudioLatency.AverageProcessingMs * count + processingLatency.TotalMilliseconds) / (count + 1);
                    
                    _currentMetrics.AudioLatency.AverageTotalLatencyMs = 
                        (_currentMetrics.AudioLatency.AverageTotalLatencyMs * count + totalLatency.TotalMilliseconds) / (count + 1);
                    
                    // Update max
                    if (totalLatency.TotalMilliseconds > _currentMetrics.AudioLatency.MaxLatencyMs)
                    {
                        _currentMetrics.AudioLatency.MaxLatencyMs = totalLatency.TotalMilliseconds;
                    }
                    
                    _currentMetrics.AudioLatency.SampleCount = count + 1;
                    
                    // Approximate P95 calculation (would need histogram for exact calculation)
                    _currentMetrics.AudioLatency.P95TotalLatencyMs = Math.Max(
                        _currentMetrics.AudioLatency.P95TotalLatencyMs,
                        totalLatency.TotalMilliseconds * 0.95);
                }
            }
        }

        /// <inheritdoc />
        public IEnumerable<PerformanceMetrics> GetHistoricalMetrics(DateTime startTime, DateTime endTime)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PerformanceMonitor));

            lock (_lockObject)
            {
                return _historicalMetrics
                    .Where(m => m.Timestamp >= startTime && m.Timestamp <= endTime)
                    .ToList(); // Return a copy to avoid concurrency issues
            }
        }

        /// <inheritdoc />
        public void ConfigureThresholds(PerformanceThresholds thresholds)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PerformanceMonitor));

            if (thresholds == null)
                throw new ArgumentNullException(nameof(thresholds));

            lock (_lockObject)
            {
                _thresholds = thresholds;
            }

            _logger.LogInformation("Performance thresholds updated: CPU={MaxCpu}%, Memory={MaxMemory}MB, Latency={MaxLatency}ms",
                thresholds.MaxCpuUsagePercent, thresholds.MaxMemoryUsageMB, thresholds.MaxAudioLatencyMs);
        }

        /// <inheritdoc />
        public PerformanceReport ExportMetrics()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PerformanceMonitor));

            lock (_lockObject)
            {
                var report = new PerformanceReport
                {
                    GeneratedAt = DateTime.UtcNow,
                    MonitoringDuration = TimeSpan.FromMilliseconds(Environment.TickCount64 - _startTime),
                    Summary = CloneMetrics(_currentMetrics),
                    ApplicationVersion = GetApplicationVersion(),
                    SystemInfo = GetSystemInfo()
                };

                // Calculate peak metrics from historical data
                if (_historicalMetrics.Count > 0)
                {
                    report.Peak = CalculatePeakMetrics();
                }

                return report;
            }
        }

        #endregion

        #region Private Methods

        private async Task MonitoringLoop(TimeSpan updateInterval, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Started performance monitoring loop");

            try
            {
                while (!cancellationToken.IsCancellationRequested && _isMonitoring)
                {
                    var metrics = CollectMetrics();
                    
                    lock (_lockObject)
                    {
                        _currentMetrics = metrics;
                        
                        // Add to historical data (keep last 1000 samples)
                        _historicalMetrics.Add(CloneMetrics(metrics));
                        if (_historicalMetrics.Count > 1000)
                        {
                            _historicalMetrics.RemoveAt(0);
                        }
                    }

                    // Check thresholds
                    CheckThresholds(metrics);

                    // Fire metrics updated event
                    MetricsUpdated?.Invoke(this, new PerformanceMetricsEventArgs(metrics));

                    await Task.Delay(updateInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in performance monitoring loop");
            }
            finally
            {
                _logger.LogDebug("Performance monitoring loop ended");
            }
        }

        private PerformanceMetrics CollectMetrics()
        {
            var metrics = new PerformanceMetrics
            {
                Timestamp = DateTime.UtcNow
            };

            try
            {
                // CPU Usage
                metrics.CpuUsagePercent = _cpuCounter.NextValue();

                // Memory Usage
                _currentProcess.Refresh();
                metrics.MemoryUsageMB = _currentProcess.WorkingSet64 / (1024.0 * 1024.0);

                // Garbage Collections (will be updated by timer)
                metrics.GarbageCollections = _currentMetrics.GarbageCollections;

                // Audio latency (current values)
                metrics.AudioLatency = _currentMetrics.AudioLatency;

                // Visualization frame rate (would be updated externally)
                metrics.VisualizationFrameRate = _currentMetrics.VisualizationFrameRate;

                // Copy timing statistics
                lock (_lockObject)
                {
                    foreach (var kvp in _timingStats)
                    {
                        metrics.Timings[kvp.Key] = kvp.Value.GetTimingMetrics();
                    }

                    // Copy counters and gauges
                    foreach (var kvp in _counters)
                    {
                        metrics.Counters[kvp.Key] = kvp.Value;
                    }

                    foreach (var kvp in _gauges)
                    {
                        metrics.Gauges[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error collecting performance metrics");
            }

            return metrics;
        }

        private void CheckThresholds(PerformanceMetrics metrics)
        {
            var thresholds = _thresholds;

            // Check CPU usage
            if (metrics.CpuUsagePercent > thresholds.MaxCpuUsagePercent)
            {
                var severity = metrics.CpuUsagePercent > thresholds.MaxCpuUsagePercent * 1.5 
                    ? ThresholdSeverity.Critical 
                    : ThresholdSeverity.Warning;

                ThresholdExceeded?.Invoke(this, new PerformanceThresholdEventArgs(
                    "CPU Usage", metrics.CpuUsagePercent, thresholds.MaxCpuUsagePercent, severity));
            }

            // Check memory usage
            if (metrics.MemoryUsageMB > thresholds.MaxMemoryUsageMB)
            {
                var severity = metrics.MemoryUsageMB > thresholds.MaxMemoryUsageMB * 1.5 
                    ? ThresholdSeverity.Critical 
                    : ThresholdSeverity.Warning;

                ThresholdExceeded?.Invoke(this, new PerformanceThresholdEventArgs(
                    "Memory Usage", metrics.MemoryUsageMB, thresholds.MaxMemoryUsageMB, severity));
            }

            // Check audio latency
            if (metrics.AudioLatency.AverageTotalLatencyMs > thresholds.MaxAudioLatencyMs)
            {
                var severity = metrics.AudioLatency.AverageTotalLatencyMs > thresholds.MaxAudioLatencyMs * 2 
                    ? ThresholdSeverity.Critical 
                    : ThresholdSeverity.Warning;

                ThresholdExceeded?.Invoke(this, new PerformanceThresholdEventArgs(
                    "Audio Latency", metrics.AudioLatency.AverageTotalLatencyMs, thresholds.MaxAudioLatencyMs, severity));
            }

            // Check visualization frame rate
            if (metrics.VisualizationFrameRate < thresholds.MinVisualizationFrameRate && metrics.VisualizationFrameRate > 0)
            {
                ThresholdExceeded?.Invoke(this, new PerformanceThresholdEventArgs(
                    "Visualization Frame Rate", metrics.VisualizationFrameRate, thresholds.MinVisualizationFrameRate, ThresholdSeverity.Warning));
            }
        }

        private void UpdateGcMetrics(object? state)
        {
            if (_disposed)
                return;

            try
            {
                int currentGcCollections = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
                int newCollections = currentGcCollections - _previousGcCollections;
                
                lock (_lockObject)
                {
                    _currentMetrics.GarbageCollections = newCollections;
                }
                
                _previousGcCollections = currentGcCollections;

                // Check GC threshold
                if (newCollections > _thresholds.MaxGarbageCollectionsPerMinute / 60) // Per second approximation
                {
                    ThresholdExceeded?.Invoke(this, new PerformanceThresholdEventArgs(
                        "Garbage Collections", newCollections * 60, _thresholds.MaxGarbageCollectionsPerMinute, ThresholdSeverity.Warning));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error updating GC metrics");
            }
        }

        private static PerformanceMetrics CloneMetrics(PerformanceMetrics source)
        {
            return new PerformanceMetrics
            {
                Timestamp = source.Timestamp,
                CpuUsagePercent = source.CpuUsagePercent,
                MemoryUsageMB = source.MemoryUsageMB,
                GarbageCollections = source.GarbageCollections,
                AudioLatency = source.AudioLatency,
                VisualizationFrameRate = source.VisualizationFrameRate,
                Timings = new Dictionary<string, TimingMetrics>(source.Timings),
                Counters = new Dictionary<string, long>(source.Counters),
                Gauges = new Dictionary<string, double>(source.Gauges)
            };
        }

        private PerformanceMetrics CalculatePeakMetrics()
        {
            var peak = new PerformanceMetrics { Timestamp = DateTime.UtcNow };

            if (_historicalMetrics.Count == 0)
                return peak;

            peak.CpuUsagePercent = _historicalMetrics.Max(m => m.CpuUsagePercent);
            peak.MemoryUsageMB = _historicalMetrics.Max(m => m.MemoryUsageMB);
            peak.GarbageCollections = _historicalMetrics.Max(m => m.GarbageCollections);
            peak.VisualizationFrameRate = _historicalMetrics.Max(m => m.VisualizationFrameRate);

            // Audio latency peaks
            peak.AudioLatency.MaxLatencyMs = _historicalMetrics.Max(m => m.AudioLatency.MaxLatencyMs);
            peak.AudioLatency.P95TotalLatencyMs = _historicalMetrics.Max(m => m.AudioLatency.P95TotalLatencyMs);

            return peak;
        }

        private static string GetApplicationVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private SystemInfo GetSystemInfo()
        {
            try
            {
                return new SystemInfo
                {
                    OperatingSystem = Environment.OSVersion.ToString(),
                    Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown",
                    TotalMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024),
                    AudioDevice = "System Default" // Would be updated with actual device info
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error collecting system information");
                return new SystemInfo();
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the performance monitor and releases all resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                try
                {
                    // Stop monitoring synchronously during disposal
                    if (_isMonitoring)
                    {
                        StopMonitoringAsync().Wait(TimeSpan.FromSeconds(2));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping monitoring during disposal");
                }

                _gcCollectionTimer?.Dispose();
                _cpuCounter?.Dispose();
                _currentProcess?.Dispose();
                _cancellationTokenSource?.Dispose();

                _logger.LogDebug("PerformanceMonitor disposed");
            }
        }

        #endregion

        #region Helper Classes

        private class TimingStatistics
        {
            private readonly List<double> _samples = new List<double>();
            private readonly object _lock = new object();

            public string OperationName { get; set; } = string.Empty;

            public void AddSample(double milliseconds)
            {
                lock (_lock)
                {
                    _samples.Add(milliseconds);
                    
                    // Keep only last 1000 samples for memory efficiency
                    if (_samples.Count > 1000)
                    {
                        _samples.RemoveAt(0);
                    }
                }
            }

            public TimingMetrics GetTimingMetrics()
            {
                lock (_lock)
                {
                    if (_samples.Count == 0)
                    {
                        return new TimingMetrics();
                    }

                    var sorted = _samples.OrderBy(x => x).ToList();
                    
                    return new TimingMetrics
                    {
                        AverageMs = _samples.Average(),
                        MinMs = _samples.Min(),
                        MaxMs = _samples.Max(),
                        P95Ms = sorted[(int)(sorted.Count * 0.95)],
                        SampleCount = _samples.Count
                    };
                }
            }
        }

        #endregion
    }
}