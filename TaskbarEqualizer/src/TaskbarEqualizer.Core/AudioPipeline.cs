using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Core.Interfaces;

namespace TaskbarEqualizer.Core
{
    /// <summary>
    /// Orchestrates the complete audio processing pipeline from capture to frequency analysis.
    /// Provides a high-level interface for managing the entire audio processing workflow.
    /// </summary>
    public sealed class AudioPipeline : IDisposable
    {
        private readonly ILogger<AudioPipeline> _logger;
        private readonly IAudioCaptureService _audioCapture;
        private readonly IFrequencyAnalyzer _frequencyAnalyzer;
        private readonly IPerformanceMonitor _performanceMonitor;
        
        private volatile bool _isRunning;
        private volatile bool _disposed;
        private readonly object _stateLock = new object();

        /// <summary>
        /// Initializes a new instance of the AudioPipeline.
        /// </summary>
        /// <param name="logger">Logger for diagnostic information.</param>
        /// <param name="audioCapture">Audio capture service.</param>
        /// <param name="frequencyAnalyzer">Frequency analysis service.</param>
        /// <param name="performanceMonitor">Performance monitoring service.</param>
        public AudioPipeline(
            ILogger<AudioPipeline> logger,
            IAudioCaptureService audioCapture,
            IFrequencyAnalyzer frequencyAnalyzer,
            IPerformanceMonitor performanceMonitor)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _audioCapture = audioCapture ?? throw new ArgumentNullException(nameof(audioCapture));
            _frequencyAnalyzer = frequencyAnalyzer ?? throw new ArgumentNullException(nameof(frequencyAnalyzer));
            _performanceMonitor = performanceMonitor ?? throw new ArgumentNullException(nameof(performanceMonitor));

            // Wire up the audio processing pipeline
            _audioCapture.AudioDataAvailable += OnAudioDataAvailable;
            _audioCapture.AudioDeviceChanged += OnAudioDeviceChanged;
            _frequencyAnalyzer.SpectrumDataAvailable += OnSpectrumDataAvailable;

            _logger.LogDebug("AudioPipeline initialized");
        }

        #region Events

        /// <summary>
        /// Event fired when new spectrum data is available from the frequency analyzer.
        /// </summary>
        public event EventHandler<SpectrumDataEventArgs>? SpectrumDataAvailable;

        /// <summary>
        /// Event fired when the audio device changes.
        /// </summary>
        public event EventHandler<AudioDeviceChangedEventArgs>? AudioDeviceChanged;

        /// <summary>
        /// Event fired when performance metrics are updated.
        /// </summary>
        public event EventHandler<PerformanceMetricsEventArgs>? PerformanceMetricsUpdated;

        /// <summary>
        /// Event fired when performance thresholds are exceeded.
        /// </summary>
        public event EventHandler<PerformanceThresholdEventArgs>? PerformanceThresholdExceeded;

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether the audio pipeline is currently running.
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Gets the current audio capture device.
        /// </summary>
        public NAudio.CoreAudioApi.MMDevice? CurrentAudioDevice => _audioCapture.CurrentDevice;

        /// <summary>
        /// Gets the current audio format being processed.
        /// </summary>
        public NAudio.Wave.WaveFormat? AudioFormat => _audioCapture.AudioFormat;

        /// <summary>
        /// Gets the current performance metrics.
        /// </summary>
        public PerformanceMetrics CurrentPerformanceMetrics => _performanceMonitor.CurrentMetrics;

        /// <summary>
        /// Gets the current number of frequency bands being analyzed.
        /// </summary>
        public int FrequencyBands => _frequencyAnalyzer.FrequencyBands;

        /// <summary>
        /// Gets the FFT size being used for analysis.
        /// </summary>
        public int FftSize => _frequencyAnalyzer.FftSize;

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts the complete audio processing pipeline with default configuration.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous start operation.</returns>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            await StartAsync(new AudioPipelineConfiguration(), cancellationToken);
        }

        /// <summary>
        /// Starts the complete audio processing pipeline with the specified configuration.
        /// </summary>
        /// <param name="configuration">Configuration for the audio pipeline.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous start operation.</returns>
        public async Task StartAsync(AudioPipelineConfiguration configuration, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioPipeline));

            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            lock (_stateLock)
            {
                if (_isRunning)
                {
                    _logger.LogWarning("Audio pipeline is already running");
                    return;
                }
            }

            _logger.LogInformation("Starting audio processing pipeline");

            try
            {
                // Start performance monitoring first
                await _performanceMonitor.StartMonitoringAsync(
                    TimeSpan.FromMilliseconds(configuration.PerformanceUpdateIntervalMs), 
                    cancellationToken);

                // Configure and start frequency analyzer
                await _frequencyAnalyzer.ConfigureAsync(
                    configuration.FftSize,
                    configuration.SampleRate,
                    configuration.FrequencyBands,
                    configuration.SmoothingFactor,
                    cancellationToken);

                await _frequencyAnalyzer.StartAnalysisAsync(cancellationToken);

                // Start audio capture (this should be last to avoid processing audio before everything is ready)
                if (configuration.AudioDevice != null)
                {
                    await _audioCapture.StartCaptureAsync(configuration.AudioDevice, cancellationToken);
                }
                else
                {
                    await _audioCapture.StartCaptureAsync(cancellationToken);
                }

                // Configure performance thresholds
                _performanceMonitor.ConfigureThresholds(configuration.PerformanceThresholds);

                // Wire up performance events
                _performanceMonitor.MetricsUpdated += OnPerformanceMetricsUpdated;
                _performanceMonitor.ThresholdExceeded += OnPerformanceThresholdExceeded;

                lock (_stateLock)
                {
                    _isRunning = true;
                }

                _logger.LogInformation("Audio processing pipeline started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start audio processing pipeline");
                
                // Attempt cleanup on failure
                try
                {
                    await StopAsync(CancellationToken.None);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Error during cleanup after failed start");
                }
                
                throw;
            }
        }

        /// <summary>
        /// Stops the complete audio processing pipeline.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous stop operation.</returns>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return;

            lock (_stateLock)
            {
                if (!_isRunning)
                {
                    _logger.LogDebug("Audio pipeline is not running");
                    return;
                }

                _isRunning = false;
            }

            _logger.LogInformation("Stopping audio processing pipeline");

            try
            {
                // Unwire performance events
                _performanceMonitor.MetricsUpdated -= OnPerformanceMetricsUpdated;
                _performanceMonitor.ThresholdExceeded -= OnPerformanceThresholdExceeded;

                // Stop services in reverse order of startup
                await _audioCapture.StopCaptureAsync(cancellationToken);
                await _frequencyAnalyzer.StopAnalysisAsync(cancellationToken);
                await _performanceMonitor.StopMonitoringAsync(cancellationToken);

                _logger.LogInformation("Audio processing pipeline stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping audio processing pipeline");
                throw;
            }
        }

        /// <summary>
        /// Switches to a different audio capture device without stopping the pipeline.
        /// </summary>
        /// <param name="device">The new audio device to use.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous switch operation.</returns>
        public async Task SwitchAudioDeviceAsync(NAudio.CoreAudioApi.MMDevice device, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioPipeline));

            if (!_isRunning)
                throw new InvalidOperationException("Pipeline must be running to switch audio devices");

            _logger.LogInformation("Switching audio device to: {DeviceName}", device?.FriendlyName ?? "Default");

            if (device == null)
                throw new ArgumentNullException(nameof(device));
                
            await _audioCapture.SwitchDeviceAsync(device, cancellationToken);
        }

        /// <summary>
        /// Updates the frequency analysis configuration without stopping the pipeline.
        /// </summary>
        /// <param name="frequencyBands">Number of frequency bands to analyze.</param>
        /// <param name="smoothingFactor">Smoothing factor for temporal stability.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous update operation.</returns>
        public async Task UpdateFrequencyAnalysisAsync(int frequencyBands, double smoothingFactor, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioPipeline));

            _logger.LogInformation("Updating frequency analysis: Bands={FrequencyBands}, Smoothing={SmoothingFactor}", 
                frequencyBands, smoothingFactor);

            await _frequencyAnalyzer.UpdateFrequencyBandsAsync(frequencyBands, cancellationToken);
            _frequencyAnalyzer.UpdateSmoothing(smoothingFactor);
        }

        /// <summary>
        /// Gets the current performance report.
        /// </summary>
        /// <returns>Current performance report.</returns>
        public PerformanceReport GetPerformanceReport()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioPipeline));

            return _performanceMonitor.ExportMetrics();
        }

        #endregion

        #region Private Event Handlers

        private async void OnAudioDataAvailable(object? sender, AudioDataEventArgs e)
        {
            try
            {
                var startTime = Environment.TickCount64;

                // Process audio through frequency analyzer
                await _frequencyAnalyzer.ProcessAudioSamplesAsync(e.Samples, e.SampleCount, e.TimestampTicks);

                // Record processing latency
                var processingLatency = TimeSpan.FromMilliseconds(Environment.TickCount64 - startTime);
                var captureLatency = TimeSpan.FromTicks(Environment.TickCount64 * 10000 - e.TimestampTicks);
                var totalLatency = captureLatency + processingLatency;

                _performanceMonitor.RecordAudioLatency(captureLatency, processingLatency, totalLatency);
                _performanceMonitor.RecordTiming("AudioProcessing", processingLatency);
                _performanceMonitor.RecordCounter("AudioSamplesProcessed", e.SampleCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio data in pipeline");
            }
        }

        private void OnAudioDeviceChanged(object? sender, AudioDeviceChangedEventArgs e)
        {
            _logger.LogInformation("Audio device changed: {PreviousDevice} -> {NewDevice}, Reason: {Reason}",
                e.PreviousDevice?.FriendlyName ?? "None",
                e.NewDevice?.FriendlyName ?? "None",
                e.Reason);

            AudioDeviceChanged?.Invoke(this, e);
        }

        private void OnSpectrumDataAvailable(object? sender, SpectrumDataEventArgs e)
        {
            try
            {
                // Record visualization frame rate
                _performanceMonitor.RecordCounter("SpectrumFrames");
                _performanceMonitor.RecordTiming("SpectrumProcessing", e.ProcessingLatency);
                _performanceMonitor.RecordGauge("SpectrumPeakValue", e.PeakValue);
                _performanceMonitor.RecordGauge("SpectrumRmsLevel", e.RmsLevel);

                // Forward the event
                SpectrumDataAvailable?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling spectrum data in pipeline");
            }
        }

        private void OnPerformanceMetricsUpdated(object? sender, PerformanceMetricsEventArgs e)
        {
            // Calculate and update visualization frame rate based on spectrum frame counter
            if (e.Metrics.Counters.TryGetValue("SpectrumFrames", out long frameCount))
            {
                // This is a simplified calculation - in reality you'd track frames over time
                e.Metrics.VisualizationFrameRate = frameCount; // Frames since last update
                _performanceMonitor.RecordGauge("VisualizationFrameRate", e.Metrics.VisualizationFrameRate);
            }

            PerformanceMetricsUpdated?.Invoke(this, e);
        }

        private void OnPerformanceThresholdExceeded(object? sender, PerformanceThresholdEventArgs e)
        {
            _logger.LogWarning("Performance threshold exceeded: {MetricName} = {CurrentValue} (threshold: {ThresholdValue}), Severity: {Severity}",
                e.MetricName, e.CurrentValue, e.ThresholdValue, e.Severity);

            PerformanceThresholdExceeded?.Invoke(this, e);
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the audio pipeline and releases all resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                try
                {
                    // Stop pipeline synchronously during disposal
                    if (_isRunning)
                    {
                        StopAsync().Wait(TimeSpan.FromSeconds(5));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping pipeline during disposal");
                }

                // Unwire events
                _audioCapture.AudioDataAvailable -= OnAudioDataAvailable;
                _audioCapture.AudioDeviceChanged -= OnAudioDeviceChanged;
                _frequencyAnalyzer.SpectrumDataAvailable -= OnSpectrumDataAvailable;

                _logger.LogDebug("AudioPipeline disposed");
            }
        }

        #endregion
    }

    /// <summary>
    /// Configuration for the audio processing pipeline.
    /// </summary>
    public class AudioPipelineConfiguration
    {
        /// <summary>
        /// FFT size for frequency analysis (must be power of 2).
        /// </summary>
        public int FftSize { get; set; } = 1024;

        /// <summary>
        /// Audio sample rate.
        /// </summary>
        public int SampleRate { get; set; } = 44100;

        /// <summary>
        /// Number of frequency bands to analyze.
        /// </summary>
        public int FrequencyBands { get; set; } = 16;

        /// <summary>
        /// Smoothing factor for temporal stability (0.0-1.0).
        /// </summary>
        public double SmoothingFactor { get; set; } = 0.8;

        /// <summary>
        /// Specific audio device to use (null for default).
        /// </summary>
        public NAudio.CoreAudioApi.MMDevice? AudioDevice { get; set; }

        /// <summary>
        /// Performance monitoring update interval in milliseconds.
        /// </summary>
        public int PerformanceUpdateIntervalMs { get; set; } = 1000;

        /// <summary>
        /// Performance thresholds for alerting.
        /// </summary>
        public PerformanceThresholds PerformanceThresholds { get; set; } = new PerformanceThresholds();
    }
}