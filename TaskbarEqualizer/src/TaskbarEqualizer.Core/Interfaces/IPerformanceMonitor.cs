using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TaskbarEqualizer.Core.Interfaces
{
    /// <summary>
    /// Interface for performance monitoring and metrics collection
    /// to ensure the application meets real-time performance requirements.
    /// </summary>
    public interface IPerformanceMonitor : IDisposable
    {
        /// <summary>
        /// Event fired when performance metrics are updated.
        /// </summary>
        event EventHandler<PerformanceMetricsEventArgs> MetricsUpdated;

        /// <summary>
        /// Event fired when performance thresholds are exceeded.
        /// </summary>
        event EventHandler<PerformanceThresholdEventArgs> ThresholdExceeded;

        /// <summary>
        /// Gets the current performance metrics snapshot.
        /// </summary>
        PerformanceMetrics CurrentMetrics { get; }

        /// <summary>
        /// Gets a value indicating whether monitoring is currently active.
        /// </summary>
        bool IsMonitoring { get; }

        /// <summary>
        /// Starts performance monitoring.
        /// </summary>
        /// <param name="updateInterval">Interval for metrics collection.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous start operation.</returns>
        Task StartMonitoringAsync(TimeSpan updateInterval, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops performance monitoring.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous stop operation.</returns>
        Task StopMonitoringAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Records a timing measurement for a specific operation.
        /// </summary>
        /// <param name="operationName">Name of the operation being measured.</param>
        /// <param name="duration">Duration of the operation.</param>
        void RecordTiming(string operationName, TimeSpan duration);

        /// <summary>
        /// Records a counter increment for a specific metric.
        /// </summary>
        /// <param name="counterName">Name of the counter.</param>
        /// <param name="increment">Value to increment by (default: 1).</param>
        void RecordCounter(string counterName, long increment = 1);

        /// <summary>
        /// Records a gauge value for a specific metric.
        /// </summary>
        /// <param name="gaugeName">Name of the gauge.</param>
        /// <param name="value">Current value of the gauge.</param>
        void RecordGauge(string gaugeName, double value);

        /// <summary>
        /// Records audio processing latency.
        /// </summary>
        /// <param name="captureToProcessingLatency">Time from audio capture to processing start.</param>
        /// <param name="processingLatency">Time spent in processing.</param>
        /// <param name="totalLatency">Total end-to-end latency.</param>
        void RecordAudioLatency(TimeSpan captureToProcessingLatency, TimeSpan processingLatency, TimeSpan totalLatency);

        /// <summary>
        /// Gets historical metrics for a specific time range.
        /// </summary>
        /// <param name="startTime">Start time for the query.</param>
        /// <param name="endTime">End time for the query.</param>
        /// <returns>Historical metrics data.</returns>
        IEnumerable<PerformanceMetrics> GetHistoricalMetrics(DateTime startTime, DateTime endTime);

        /// <summary>
        /// Configures performance thresholds for alerting.
        /// </summary>
        /// <param name="thresholds">Performance threshold configuration.</param>
        void ConfigureThresholds(PerformanceThresholds thresholds);

        /// <summary>
        /// Exports current metrics to a structured format.
        /// </summary>
        /// <returns>Metrics in exportable format.</returns>
        PerformanceReport ExportMetrics();
    }

    /// <summary>
    /// Performance metrics data structure.
    /// </summary>
    public class PerformanceMetrics
    {
        /// <summary>
        /// Timestamp when these metrics were collected.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// CPU usage percentage (0-100).
        /// </summary>
        public double CpuUsagePercent { get; set; }

        /// <summary>
        /// Memory usage in megabytes.
        /// </summary>
        public double MemoryUsageMB { get; set; }

        /// <summary>
        /// Number of garbage collections since last measurement.
        /// </summary>
        public int GarbageCollections { get; set; }

        /// <summary>
        /// Audio processing latency metrics.
        /// </summary>
        public AudioLatencyMetrics AudioLatency { get; set; } = new();

        /// <summary>
        /// Frame rate for visualization updates.
        /// </summary>
        public double VisualizationFrameRate { get; set; }

        /// <summary>
        /// Custom timing measurements.
        /// </summary>
        public Dictionary<string, TimingMetrics> Timings { get; set; } = new();

        /// <summary>
        /// Custom counter values.
        /// </summary>
        public Dictionary<string, long> Counters { get; set; } = new();

        /// <summary>
        /// Custom gauge values.
        /// </summary>
        public Dictionary<string, double> Gauges { get; set; } = new();
    }

    /// <summary>
    /// Audio latency specific metrics.
    /// </summary>
    public class AudioLatencyMetrics
    {
        /// <summary>
        /// Average capture to processing latency in milliseconds.
        /// </summary>
        public double AverageCaptureToProcessingMs { get; set; }

        /// <summary>
        /// Average processing latency in milliseconds.
        /// </summary>
        public double AverageProcessingMs { get; set; }

        /// <summary>
        /// Average total end-to-end latency in milliseconds.
        /// </summary>
        public double AverageTotalLatencyMs { get; set; }

        /// <summary>
        /// 95th percentile total latency in milliseconds.
        /// </summary>
        public double P95TotalLatencyMs { get; set; }

        /// <summary>
        /// Maximum recorded latency in milliseconds.
        /// </summary>
        public double MaxLatencyMs { get; set; }

        /// <summary>
        /// Number of latency measurements taken.
        /// </summary>
        public long SampleCount { get; set; }
    }

    /// <summary>
    /// Timing metrics for specific operations.
    /// </summary>
    public class TimingMetrics
    {
        /// <summary>
        /// Average duration in milliseconds.
        /// </summary>
        public double AverageMs { get; set; }

        /// <summary>
        /// Minimum duration in milliseconds.
        /// </summary>
        public double MinMs { get; set; }

        /// <summary>
        /// Maximum duration in milliseconds.
        /// </summary>
        public double MaxMs { get; set; }

        /// <summary>
        /// 95th percentile duration in milliseconds.
        /// </summary>
        public double P95Ms { get; set; }

        /// <summary>
        /// Number of timing samples.
        /// </summary>
        public long SampleCount { get; set; }
    }

    /// <summary>
    /// Performance threshold configuration.
    /// </summary>
    public class PerformanceThresholds
    {
        /// <summary>
        /// Maximum acceptable CPU usage percentage.
        /// </summary>
        public double MaxCpuUsagePercent { get; set; } = 5.0;

        /// <summary>
        /// Maximum acceptable memory usage in MB.
        /// </summary>
        public double MaxMemoryUsageMB { get; set; } = 50.0;

        /// <summary>
        /// Maximum acceptable total audio latency in milliseconds.
        /// </summary>
        public double MaxAudioLatencyMs { get; set; } = 100.0;

        /// <summary>
        /// Minimum acceptable visualization frame rate.
        /// </summary>
        public double MinVisualizationFrameRate { get; set; } = 30.0;

        /// <summary>
        /// Maximum acceptable garbage collections per minute.
        /// </summary>
        public int MaxGarbageCollectionsPerMinute { get; set; } = 10;
    }

    /// <summary>
    /// Event arguments for performance metrics updates.
    /// </summary>
    public class PerformanceMetricsEventArgs : EventArgs
    {
        /// <summary>
        /// Current performance metrics.
        /// </summary>
        public PerformanceMetrics Metrics { get; }

        public PerformanceMetricsEventArgs(PerformanceMetrics metrics)
        {
            Metrics = metrics;
        }
    }

    /// <summary>
    /// Event arguments for performance threshold violations.
    /// </summary>
    public class PerformanceThresholdEventArgs : EventArgs
    {
        /// <summary>
        /// Name of the metric that exceeded the threshold.
        /// </summary>
        public string MetricName { get; }

        /// <summary>
        /// Current value of the metric.
        /// </summary>
        public double CurrentValue { get; }

        /// <summary>
        /// Threshold value that was exceeded.
        /// </summary>
        public double ThresholdValue { get; }

        /// <summary>
        /// Severity of the threshold violation.
        /// </summary>
        public ThresholdSeverity Severity { get; }

        public PerformanceThresholdEventArgs(string metricName, double currentValue, double thresholdValue, ThresholdSeverity severity)
        {
            MetricName = metricName;
            CurrentValue = currentValue;
            ThresholdValue = thresholdValue;
            Severity = severity;
        }
    }

    /// <summary>
    /// Severity levels for threshold violations.
    /// </summary>
    public enum ThresholdSeverity
    {
        /// <summary>
        /// Informational - metric approaching threshold.
        /// </summary>
        Info,

        /// <summary>
        /// Warning - metric exceeded threshold but system still functional.
        /// </summary>
        Warning,

        /// <summary>
        /// Critical - metric significantly exceeded threshold, may impact functionality.
        /// </summary>
        Critical
    }

    /// <summary>
    /// Performance report for export and analysis.
    /// </summary>
    public class PerformanceReport
    {
        /// <summary>
        /// Report generation timestamp.
        /// </summary>
        public DateTime GeneratedAt { get; set; }

        /// <summary>
        /// Duration of the monitoring period.
        /// </summary>
        public TimeSpan MonitoringDuration { get; set; }

        /// <summary>
        /// Summary statistics for the monitoring period.
        /// </summary>
        public PerformanceMetrics Summary { get; set; } = new();

        /// <summary>
        /// Peak performance metrics during the period.
        /// </summary>
        public PerformanceMetrics Peak { get; set; } = new();

        /// <summary>
        /// Threshold violations that occurred.
        /// </summary>
        public List<PerformanceThresholdEventArgs> ThresholdViolations { get; set; } = new();

        /// <summary>
        /// Application version information.
        /// </summary>
        public string ApplicationVersion { get; set; } = string.Empty;

        /// <summary>
        /// System information.
        /// </summary>
        public SystemInfo SystemInfo { get; set; } = new();
    }

    /// <summary>
    /// System information for performance reports.
    /// </summary>
    public class SystemInfo
    {
        /// <summary>
        /// Operating system version.
        /// </summary>
        public string OperatingSystem { get; set; } = string.Empty;

        /// <summary>
        /// Processor information.
        /// </summary>
        public string Processor { get; set; } = string.Empty;

        /// <summary>
        /// Total system memory in MB.
        /// </summary>
        public long TotalMemoryMB { get; set; }

        /// <summary>
        /// Audio device information.
        /// </summary>
        public string AudioDevice { get; set; } = string.Empty;
    }
}