using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using FftSharp;
using TaskbarEqualizer.Core.Interfaces;
using TaskbarEqualizer.Core.DataStructures;

namespace TaskbarEqualizer.Core.Audio
{
    /// <summary>
    /// High-performance frequency analyzer using FFT for real-time audio spectrum analysis.
    /// Optimized for minimal latency and CPU usage with configurable frequency bands and smoothing.
    /// </summary>
    public sealed class FrequencyAnalyzer : IFrequencyAnalyzer
    {
        private readonly ILogger<FrequencyAnalyzer> _logger;
        private readonly SpectrumDataPool _spectrumPool;
        private readonly AudioSamplePool _samplePool;
        private readonly LockFreeQueue<AudioProcessingItem> _processingQueue;
        
        private FrequencyAnalysisConfiguration _config;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _processingTask;
        
        // FFT processing members
        private double[]? _window;
        private double[]? _fftBuffer;
        private double[]? _magnitudeBuffer;
        private double[]? _previousSpectrum;
        private double[]? _currentSpectrum;
        private double[]? _smoothingBuffer;
        
        // Frequency band mapping
        private int[]? _bandMappings;
        private double[]? _bandWeights;
        
        // Performance optimization
        private readonly object _configLock = new object();
        private volatile bool _isAnalyzing;
        private volatile bool _disposed;
        private volatile bool _configurationDirty = true;
        
        // Timing and statistics
        private long _totalProcessedSamples;
        private double _averageProcessingTimeMs;
        private int _processingCount;

        /// <summary>
        /// Initializes a new instance of the FrequencyAnalyzer.
        /// </summary>
        /// <param name="logger">Logger for diagnostic information.</param>
        public FrequencyAnalyzer(ILogger<FrequencyAnalyzer> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Initialize with default configuration
            _config = new FrequencyAnalysisConfiguration();
            
            // Create object pools for memory optimization
            _spectrumPool = PoolFactory.CreateSpectrumDataPool(_config.FrequencyBands);
            _samplePool = PoolFactory.CreateAudioSamplePool(44100, 23.0); // Default for 1024 samples
            
            // Create processing queue for thread-safe audio data handling
            _processingQueue = LockFreeQueueFactory.CreateForAudioProcessing<AudioProcessingItem>(50.0, 44100);
            
            _logger.LogDebug("FrequencyAnalyzer initialized with default configuration");
        }

        #region Events

        /// <inheritdoc />
        public event EventHandler<SpectrumDataEventArgs>? SpectrumDataAvailable;

        #endregion

        #region Properties

        /// <inheritdoc />
        public int FrequencyBands => _config.FrequencyBands;

        /// <inheritdoc />
        public int FftSize => _config.FftSize;

        /// <inheritdoc />
        public int SampleRate { get; private set; } = 44100;

        /// <inheritdoc />
        public bool IsAnalyzing => _isAnalyzing;

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public async Task ConfigureAsync(int fftSize, int sampleRate, int frequencyBands, double smoothingFactor, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FrequencyAnalyzer));

            if (fftSize <= 0 || (fftSize & (fftSize - 1)) != 0)
                throw new ArgumentException("FFT size must be a positive power of 2", nameof(fftSize));

            if (sampleRate <= 0)
                throw new ArgumentException("Sample rate must be positive", nameof(sampleRate));

            if (frequencyBands < 8 || frequencyBands > 32)
                throw new ArgumentException("Frequency bands must be between 8 and 32", nameof(frequencyBands));

            if (smoothingFactor < 0.0 || smoothingFactor > 1.0)
                throw new ArgumentException("Smoothing factor must be between 0.0 and 1.0", nameof(smoothingFactor));

            _logger.LogInformation("Configuring frequency analyzer: FFT={FftSize}, SampleRate={SampleRate}, Bands={FrequencyBands}, Smoothing={SmoothingFactor}",
                fftSize, sampleRate, frequencyBands, smoothingFactor);

            lock (_configLock)
            {
                _config.FftSize = fftSize;
                _config.FrequencyBands = frequencyBands;
                _config.SmoothingFactor = smoothingFactor;
                SampleRate = sampleRate;
                _configurationDirty = true;
            }

            await Task.Run(() => InitializeProcessingBuffers(), cancellationToken);
            
            _logger.LogDebug("Frequency analyzer configuration completed");
        }

        /// <inheritdoc />
        public async Task ProcessAudioSamplesAsync(float[] samples, int sampleCount, long timestampTicks)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FrequencyAnalyzer));

            if (samples == null)
                throw new ArgumentNullException(nameof(samples));

            if (sampleCount <= 0 || sampleCount > samples.Length)
                throw new ArgumentOutOfRangeException(nameof(sampleCount));

            if (!_isAnalyzing)
                return;

            // Create processing item and queue it
            var processingItem = new AudioProcessingItem
            {
                Samples = _samplePool.Get(),
                SampleCount = Math.Min(sampleCount, _config.FftSize),
                TimestampTicks = timestampTicks
            };

            // Copy samples to our internal buffer
            Array.Copy(samples, 0, processingItem.Samples, 0, processingItem.SampleCount);

            // Queue for processing (non-blocking)
            if (!_processingQueue.TryEnqueue(processingItem))
            {
                // Queue is full, drop this sample to maintain real-time performance
                _samplePool.Return(processingItem.Samples);
                _logger.LogDebug("Dropped audio sample due to full processing queue");
            }

            await Task.CompletedTask; // Make method async without actual async work
        }

        /// <inheritdoc />
        public async Task StartAnalysisAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FrequencyAnalyzer));

            if (_isAnalyzing)
            {
                _logger.LogWarning("Frequency analysis is already running");
                return;
            }

            _logger.LogInformation("Starting frequency analysis");

            try
            {
                // Ensure buffers are initialized
                if (_configurationDirty)
                {
                    InitializeProcessingBuffers();
                }

                // Create cancellation token for this analysis session
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                // Start processing task
                _processingTask = Task.Run(() => ProcessingLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

                _isAnalyzing = true;
                _logger.LogInformation("Frequency analysis started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start frequency analysis");
                throw;
            }

            await Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task StopAnalysisAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return;

            if (!_isAnalyzing)
            {
                _logger.LogDebug("Frequency analysis is not running");
                return;
            }

            _logger.LogInformation("Stopping frequency analysis");

            try
            {
                // Signal cancellation
                _cancellationTokenSource?.Cancel();
                _isAnalyzing = false;

                // Wait for processing task to complete
                if (_processingTask != null)
                {
                    await _processingTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping frequency analysis");
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _processingTask = null;
            }

            _logger.LogInformation("Frequency analysis stopped");
        }

        /// <inheritdoc />
        public void UpdateSmoothing(double smoothingFactor, double attackTime = 10.0, double decayTime = 100.0)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FrequencyAnalyzer));

            if (smoothingFactor < 0.0 || smoothingFactor > 1.0)
                throw new ArgumentOutOfRangeException(nameof(smoothingFactor));

            lock (_configLock)
            {
                _config.SmoothingFactor = smoothingFactor;
                _config.AttackTime = attackTime;
                _config.DecayTime = decayTime;
            }

            _logger.LogDebug("Updated smoothing: Factor={SmoothingFactor}, Attack={AttackTime}ms, Decay={DecayTime}ms",
                smoothingFactor, attackTime, decayTime);
        }

        /// <inheritdoc />
        public async Task UpdateFrequencyBandsAsync(int frequencyBands, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FrequencyAnalyzer));

            if (frequencyBands < 8 || frequencyBands > 32)
                throw new ArgumentOutOfRangeException(nameof(frequencyBands));

            _logger.LogInformation("Updating frequency bands to {FrequencyBands}", frequencyBands);

            lock (_configLock)
            {
                _config.FrequencyBands = frequencyBands;
                _configurationDirty = true;
            }

            await Task.Run(() =>
            {
                if (_isAnalyzing)
                {
                    InitializeBandMapping();
                    InitializeSpectrumBuffers();
                }
            }, cancellationToken);

            _logger.LogDebug("Frequency bands updated successfully");
        }

        #endregion

        #region Private Methods

        private void InitializeProcessingBuffers()
        {
            lock (_configLock)
            {
                _logger.LogDebug("Initializing processing buffers for FFT size {FftSize}", _config.FftSize);

                // Initialize FFT buffers
                _fftBuffer = new double[_config.FftSize];
                _magnitudeBuffer = new double[_config.FftSize / 2];

                // Initialize window function
                InitializeWindowFunction();

                // Initialize frequency band mapping
                InitializeBandMapping();

                // Initialize spectrum buffers
                InitializeSpectrumBuffers();

                _configurationDirty = false;
            }
        }

        private void InitializeWindowFunction()
        {
            _window = new double[_config.FftSize];

            switch (_config.WindowFunction)
            {
                case WindowFunction.Hann:
                    FftSharp.Window.Hann(_window);
                    break;
                case WindowFunction.Hamming:
                    FftSharp.Window.Hamming(_window);
                    break;
                case WindowFunction.Blackman:
                    FftSharp.Window.Blackman(_window);
                    break;
                case WindowFunction.Kaiser:
                    FftSharp.Window.Kaiser(_window, 5.0); // Beta = 5.0
                    break;
                case WindowFunction.None:
                default:
                    Array.Fill(_window, 1.0);
                    break;
            }

            _logger.LogDebug("Initialized {WindowFunction} window function", _config.WindowFunction);
        }

        private void InitializeBandMapping()
        {
            _bandMappings = new int[_config.FrequencyBands];
            _bandWeights = new double[_config.FrequencyBands];

            double nyquist = SampleRate / 2.0;
            double minFreq = _config.MinFrequency;
            double maxFreq = Math.Min(_config.MaxFrequency, nyquist);

            if (_config.UseLogarithmicScaling)
            {
                // Logarithmic frequency distribution
                double logMin = Math.Log10(minFreq);
                double logMax = Math.Log10(maxFreq);
                double logStep = (logMax - logMin) / _config.FrequencyBands;

                for (int band = 0; band < _config.FrequencyBands; band++)
                {
                    double logFreq = logMin + band * logStep;
                    double frequency = Math.Pow(10, logFreq);
                    int fftBin = (int)Math.Round(frequency * _config.FftSize / SampleRate);
                    
                    _bandMappings[band] = Math.Min(fftBin, _magnitudeBuffer!.Length - 1);
                    _bandWeights[band] = 1.0; // Equal weighting for now
                }
            }
            else
            {
                // Linear frequency distribution
                double freqStep = (maxFreq - minFreq) / _config.FrequencyBands;

                for (int band = 0; band < _config.FrequencyBands; band++)
                {
                    double frequency = minFreq + band * freqStep;
                    int fftBin = (int)Math.Round(frequency * _config.FftSize / SampleRate);
                    
                    _bandMappings[band] = Math.Min(fftBin, _magnitudeBuffer!.Length - 1);
                    _bandWeights[band] = 1.0;
                }
            }

            _logger.LogDebug("Initialized {ScalingType} frequency band mapping for {BandCount} bands",
                _config.UseLogarithmicScaling ? "logarithmic" : "linear", _config.FrequencyBands);
        }

        private void InitializeSpectrumBuffers()
        {
            _currentSpectrum = new double[_config.FrequencyBands];
            _previousSpectrum = new double[_config.FrequencyBands];
            _smoothingBuffer = new double[_config.FrequencyBands];

            Array.Clear(_currentSpectrum);
            Array.Clear(_previousSpectrum);
            Array.Clear(_smoothingBuffer);
        }

        private void ProcessingLoop(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Started frequency analysis processing loop");

            try
            {
                while (!cancellationToken.IsCancellationRequested && _isAnalyzing)
                {
                    // Process available audio samples
                    while (_processingQueue.TryDequeue(out AudioProcessingItem? item))
                    {
                        if (item != null)
                        {
                            ProcessAudioSample(item);
                        }
                    }

                    // Small delay to prevent excessive CPU usage
                    Thread.Sleep(1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in frequency analysis processing loop");
            }
            finally
            {
                // Clean up any remaining items in the queue
                while (_processingQueue.TryDequeue(out AudioProcessingItem? item))
                {
                    if (item?.Samples != null)
                    {
                        _samplePool.Return(item.Samples);
                    }
                }

                _logger.LogDebug("Frequency analysis processing loop ended");
            }
        }

        private void ProcessAudioSample(AudioProcessingItem item)
        {
            var startTime = Environment.TickCount64;

            try
            {
                // Apply windowing and copy to FFT buffer
                for (int i = 0; i < Math.Min(item.SampleCount, _config.FftSize); i++)
                {
                    _fftBuffer![i] = item.Samples[i] * _window![i];
                }

                // Zero-pad if necessary
                for (int i = item.SampleCount; i < _config.FftSize; i++)
                {
                    _fftBuffer![i] = 0.0;
                }

                // Perform FFT
                var fftResult = FftSharp.FFT.Forward(_fftBuffer);

                // Calculate magnitudes
                for (int i = 0; i < _magnitudeBuffer!.Length; i++)
                {
                    var real = fftResult[i].Real;
                    var imag = fftResult[i].Imaginary;
                    _magnitudeBuffer[i] = Math.Sqrt(real * real + imag * imag);
                }

                // Map FFT bins to frequency bands
                MapFrequencyBands();

                // Apply smoothing
                ApplySmoothing();

                // Calculate statistics
                CalculateSpectrumStatistics(out double peakValue, out int peakBandIndex, out double rmsLevel);

                // Get spectrum array from pool and copy data
                var spectrum = _spectrumPool.Get();
                Array.Copy(_currentSpectrum!, spectrum, Math.Min(_currentSpectrum!.Length, spectrum.Length));

                // Calculate processing latency
                var processingLatency = TimeSpan.FromMilliseconds(Environment.TickCount64 - startTime);

                // Fire event
                SpectrumDataAvailable?.Invoke(this, new SpectrumDataEventArgs(
                    spectrum, _config.FrequencyBands, peakValue, peakBandIndex, 
                    item.TimestampTicks, processingLatency, rmsLevel));

                // Update statistics
                UpdateProcessingStatistics(processingLatency.TotalMilliseconds);
                _totalProcessedSamples += item.SampleCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio sample");
            }
            finally
            {
                // Return sample buffer to pool
                _samplePool.Return(item.Samples);
            }
        }

        private void MapFrequencyBands()
        {
            Array.Clear(_currentSpectrum!);

            for (int band = 0; band < _config.FrequencyBands; band++)
            {
                int fftBin = _bandMappings![band];
                double magnitude = _magnitudeBuffer![fftBin];
                
                // Apply logarithmic scaling for better visualization
                if (magnitude > 0)
                {
                    _currentSpectrum[band] = Math.Log10(1 + magnitude * 9) * _bandWeights![band];
                }
            }

            // Normalize to 0-1 range
            NormalizeSpectrum();
        }

        private void NormalizeSpectrum()
        {
            double maxValue = 0.0;
            for (int i = 0; i < _currentSpectrum!.Length; i++)
            {
                if (_currentSpectrum[i] > maxValue)
                    maxValue = _currentSpectrum[i];
            }

            if (maxValue > 0.0)
            {
                double scale = 1.0 / maxValue;
                for (int i = 0; i < _currentSpectrum.Length; i++)
                {
                    _currentSpectrum[i] *= scale;
                }
            }
        }

        private void ApplySmoothing()
        {
            double smoothing = _config.SmoothingFactor;
            double attackFactor = 1.0 - Math.Exp(-1.0 / (_config.AttackTime * SampleRate / 1000.0));
            double decayFactor = 1.0 - Math.Exp(-1.0 / (_config.DecayTime * SampleRate / 1000.0));

            for (int i = 0; i < _config.FrequencyBands; i++)
            {
                double current = _currentSpectrum![i];
                double previous = _previousSpectrum![i];

                // Use different smoothing for attack vs decay
                double factor = current > previous ? attackFactor : decayFactor;
                double smoothed = previous + factor * (current - previous);

                _smoothingBuffer![i] = smoothed;
                _previousSpectrum[i] = smoothed;
            }

            // Copy smoothed values back to current spectrum
            Array.Copy(_smoothingBuffer!, _currentSpectrum!, _config.FrequencyBands);
        }

        private void CalculateSpectrumStatistics(out double peakValue, out int peakBandIndex, out double rmsLevel)
        {
            peakValue = 0.0;
            peakBandIndex = 0;
            double sumSquares = 0.0;

            for (int i = 0; i < _config.FrequencyBands; i++)
            {
                double value = _currentSpectrum![i];
                
                if (value > peakValue)
                {
                    peakValue = value;
                    peakBandIndex = i;
                }

                sumSquares += value * value;
            }

            rmsLevel = Math.Sqrt(sumSquares / _config.FrequencyBands);
        }

        private void UpdateProcessingStatistics(double processingTimeMs)
        {
            _processingCount++;
            _averageProcessingTimeMs = (_averageProcessingTimeMs * (_processingCount - 1) + processingTimeMs) / _processingCount;

            // Log performance statistics periodically
            if (_processingCount % 1000 == 0)
            {
                _logger.LogDebug("Processing stats: Avg={AvgProcessingTime:F2}ms, Samples={TotalSamples}, Count={ProcessingCount}",
                    _averageProcessingTimeMs, _totalProcessedSamples, _processingCount);
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the frequency analyzer and releases all resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                try
                {
                    // Stop analysis synchronously during disposal
                    if (_isAnalyzing)
                    {
                        StopAnalysisAsync().Wait(TimeSpan.FromSeconds(2));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping analysis during disposal");
                }

                _processingQueue?.Dispose();
                _spectrumPool?.Dispose();
                _samplePool?.Dispose();
                _cancellationTokenSource?.Dispose();

                _logger.LogDebug("FrequencyAnalyzer disposed");
            }
        }

        #endregion

        #region Internal Classes

        private class AudioProcessingItem
        {
            public float[] Samples { get; set; } = Array.Empty<float>();
            public int SampleCount { get; set; }
            public long TimestampTicks { get; set; }
        }

        #endregion
    }
}