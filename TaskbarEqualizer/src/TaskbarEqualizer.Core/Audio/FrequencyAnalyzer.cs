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
        private AudioSamplePool _samplePool;
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
        
        // Noise floor and silence detection
        private DateTime _lastSilenceDetection = DateTime.MinValue;
        private readonly double[] _recentNoiseFloorSamples = new double[10];
        private int _noiseFloorSampleIndex = 0;
        
        // Moving average buffers for stability
        private CircularBuffer<double>[]? _movingAverageBuffers;

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
            // Use max buffer size to handle variable audio capture sizes, but ensure it's at least FFT size
            int maxBufferSize = Math.Max(_config.FftSize, 2048); 
            _samplePool = PoolFactory.CreateAudioSamplePoolForFft(maxBufferSize);
            
            // Create processing queue for thread-safe audio data handling
            _processingQueue = LockFreeQueueFactory.CreateForAudioProcessing<AudioProcessingItem>(50.0, 44100);
            
            _logger.LogDebug("FrequencyAnalyzer initialized with sample pool size: {MaxBufferSize} (FFT: {FftSize})", maxBufferSize, _config.FftSize);
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
        public async Task ProcessAudioSamplesAsync(float[] samples, int sampleCount, long timestampTicks)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FrequencyAnalyzer));

            if (samples == null)
                throw new ArgumentNullException(nameof(samples));

            if (sampleCount <= 0 || sampleCount > samples.Length)
                throw new ArgumentOutOfRangeException(nameof(sampleCount));

            if (!_isAnalyzing)
            {
                _logger.LogDebug("Frequency analyzer not running - ignoring audio samples");
                return;
            }

            try
            {
                // Get a buffer from the pool
                var pooledSamples = _samplePool.Get();
                if (pooledSamples == null)
                {
                    _logger.LogWarning("Failed to get sample buffer from pool - dropping audio data");
                    return;
                }

                // Copy the audio samples (only copy what fits)
                var copyCount = Math.Min(sampleCount, pooledSamples.Length);
                Array.Copy(samples, pooledSamples, copyCount);

                // Create processing item and queue it
                var processingItem = new AudioProcessingItem 
                { 
                    Samples = pooledSamples, 
                    SampleCount = copyCount, 
                    TimestampTicks = timestampTicks 
                };
                
                if (!_processingQueue.TryEnqueue(processingItem))
                {
                    _logger.LogDebug("Audio processing queue full - dropping samples");
                    _samplePool.Return(pooledSamples);
                    return;
                }

                // Suppress frequent debug logging - audio queuing is working normally
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error queuing audio samples for processing");
            }

            await Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task ConfigureAsync(int fftSize, int sampleRate, int frequencyBands, double smoothingFactor, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FrequencyAnalyzer));

            if (fftSize <= 0 || (fftSize & (fftSize - 1)) != 0)
                throw new ArgumentException("FFT size must be a positive power of 2", nameof(fftSize));

            if (sampleRate <= 0)
                throw new ArgumentException("Sample rate must be positive", nameof(sampleRate));

            if (frequencyBands < 4 || frequencyBands > 64)
                throw new ArgumentException("Frequency bands must be between 4 and 64", nameof(frequencyBands));

            if (smoothingFactor < 0.0 || smoothingFactor > 1.0)
                throw new ArgumentException("Smoothing factor must be between 0.0 and 1.0", nameof(smoothingFactor));

            _logger.LogInformation("Configuring frequency analyzer: FFT={FftSize}, SampleRate={SampleRate}, Bands={FrequencyBands}, Smoothing={SmoothingFactor}",
                fftSize, sampleRate, frequencyBands, smoothingFactor);

            lock (_configLock)
            {
                // Check if FFT size is changing - need to recreate sample pool
                bool fftSizeChanged = _config.FftSize != fftSize;
                
                _config.FftSize = fftSize;
                _config.FrequencyBands = frequencyBands;
                _config.SmoothingFactor = smoothingFactor;
                SampleRate = sampleRate;
                _configurationDirty = true;
                
                // Recreate sample pool if FFT size changed
                if (fftSizeChanged)
                {
                    _samplePool?.Dispose();
                    int maxBufferSize = Math.Max(_config.FftSize, 2048);
                    _samplePool = PoolFactory.CreateAudioSamplePoolForFft(maxBufferSize);
                    _logger.LogDebug("Recreated sample pool for new FFT size: {FftSize} (max buffer: {MaxBufferSize})", _config.FftSize, maxBufferSize);
                }
            }

            await Task.Run(() => InitializeProcessingBuffers(), cancellationToken);
            
            _logger.LogDebug("Frequency analyzer configuration completed");
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

            if (frequencyBands < 4 || frequencyBands > 64)
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
                    // Create a simple Hann window manually
                    for (int i = 0; i < _config.FftSize; i++)
                    {
                        _window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (_config.FftSize - 1)));
                    }
                    break;
                case WindowFunction.Hamming:
                    // Create a simple Hamming window manually
                    for (int i = 0; i < _config.FftSize; i++)
                    {
                        _window[i] = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (_config.FftSize - 1));
                    }
                    break;
                case WindowFunction.Blackman:
                    // Create a simple Blackman window manually
                    for (int i = 0; i < _config.FftSize; i++)
                    {
                        _window[i] = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (_config.FftSize - 1)) + 
                                   0.08 * Math.Cos(4 * Math.PI * i / (_config.FftSize - 1));
                    }
                    break;
                case WindowFunction.Kaiser:
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
            
            // Initialize moving average buffers
            _movingAverageBuffers = new CircularBuffer<double>[_config.FrequencyBands];
            for (int i = 0; i < _config.FrequencyBands; i++)
            {
                _movingAverageBuffers[i] = new CircularBuffer<double>(_config.MovingAverageSize);
            }

            Array.Clear(_currentSpectrum);
            Array.Clear(_previousSpectrum);
            Array.Clear(_smoothingBuffer);
            
            // Reset silence detection
            _lastSilenceDetection = DateTime.MinValue;
        }

        private void ProcessingLoop(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Started frequency analysis processing loop");

            try
            {
                while (!cancellationToken.IsCancellationRequested && _isAnalyzing)
                {
                    // Process available audio samples immediately
                    bool processed = false;
                    while (_processingQueue.TryDequeue(out AudioProcessingItem? item))
                    {
                        if (item != null)
                        {
                            ProcessAudioSample(item);
                            processed = true;
                        }
                    }

                    // Only sleep if no work was done
                    if (!processed)
                    {
                        Thread.Sleep(1);
                    }
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
                // Suppress frequent debug logging - processing is working normally
                // Ensure buffers are initialized
                if (_fftBuffer == null || _window == null || _magnitudeBuffer == null || _currentSpectrum == null)
                {
                    _logger.LogError("Processing buffers not initialized properly");
                    return;
                }
                
                // Apply windowing and copy to FFT buffer
                var samplesToProcess = Math.Min(item.SampleCount, _config.FftSize);
                // Processing samples - debug logging suppressed for performance
                
                for (int i = 0; i < samplesToProcess; i++)
                {
                    _fftBuffer[i] = item.Samples[i] * _window[i];
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
                
                // Check for prolonged silence and reset buffers if needed
                CheckForSilenceReset(rmsLevel);

                // Get spectrum array from pool and copy data
                var spectrum = _spectrumPool.Get();
                if (spectrum == null)
                {
                    _logger.LogWarning("Failed to get spectrum buffer from pool");
                    return;
                }
                
                Array.Copy(_currentSpectrum!, spectrum, Math.Min(_currentSpectrum!.Length, spectrum.Length));

                // Calculate processing latency
                var processingLatency = TimeSpan.FromMilliseconds(Environment.TickCount64 - startTime);

                // Fire event - spectrum data ready
                SpectrumDataAvailable?.Invoke(this, new SpectrumDataEventArgs(
                    spectrum, _config.FrequencyBands, peakValue, peakBandIndex, 
                    item.TimestampTicks, processingLatency, rmsLevel));

                // Update statistics
                UpdateProcessingStatistics(processingLatency.TotalMilliseconds);
                _totalProcessedSamples += item.SampleCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CRITICAL ERROR processing audio sample at step: {Step}", "unknown");
                // Log detailed state for debugging
                _logger.LogError("Buffer states - FFT: {FFTNull}, Window: {WindowNull}, Magnitude: {MagNull}, Current: {CurNull}",
                    _fftBuffer == null, _window == null, _magnitudeBuffer == null, _currentSpectrum == null);
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
            
            // Calculate adaptive noise floor
            double noiseFloor = _config.UseAdaptiveNoiseFloor ? CalculateAdaptiveNoiseFloor() : _config.NoiseFloor;

            for (int band = 0; band < _config.FrequencyBands; band++)
            {
                int fftBin = _bandMappings![band];
                double magnitude = _magnitudeBuffer![fftBin];
                
                // Apply noise gate BEFORE logarithmic scaling
                if (magnitude < noiseFloor)
                {
                    _currentSpectrum![band] = 0.0;
                    continue;
                }
                
                // Apply logarithmic scaling for better visualization
                _currentSpectrum![band] = Math.Log10(1 + magnitude * 9) * _bandWeights![band];
            }

            // Apply moving average for stability
            ApplyMovingAverage();
            
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
            // Use proper smoothing coefficients (lower = more responsive)
            double smoothingFactor = _config.SmoothingFactor;
            
            // For better responsiveness, use a lower smoothing factor
            // Also fix the attack/decay logic
            double attackCoeff = 0.8; // Fast rise (lower = faster)
            double decayCoeff = 0.3; // Fast fall (lower = faster)
            
            for (int i = 0; i < _config.FrequencyBands; i++)
            {
                double current = _currentSpectrum![i];
                double previous = _previousSpectrum![i];
                
                double smoothed;
                if (current > previous)
                {
                    // Attack: Fast rise - use less smoothing
                    smoothed = previous + (1.0 - attackCoeff) * (current - previous);
                }
                else
                {
                    // Decay: Fast fall
                    smoothed = previous * decayCoeff + current * (1.0 - decayCoeff);
                }
                
                // Force to zero when very small to prevent accumulation
                if (smoothed < 0.001)
                    smoothed = 0.0;
                
                _smoothingBuffer![i] = smoothed;
            }

            // Copy smoothed values back
            Array.Copy(_smoothingBuffer!, _currentSpectrum!, _config.FrequencyBands);
            Array.Copy(_smoothingBuffer!, _previousSpectrum!, _config.FrequencyBands);
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
        
        private double CalculateAdaptiveNoiseFloor()
        {
            // Calculate noise floor from quiet frequency bins (lower frequencies are usually quieter in background noise)
            double sum = 0.0;
            int count = 0;
            int binsToCheck = Math.Min(_magnitudeBuffer!.Length / 8, 32); // Check lower frequency bins
            
            for (int i = 1; i < binsToCheck; i++) // Skip DC component at index 0
            {
                double magnitude = _magnitudeBuffer[i];
                if (magnitude > 0.0001) // Only consider non-zero values
                {
                    sum += magnitude;
                    count++;
                }
            }
            
            double average = count > 0 ? sum / count : 0.001;
            
            // Store in circular buffer for stability
            _recentNoiseFloorSamples[_noiseFloorSampleIndex] = average;
            _noiseFloorSampleIndex = (_noiseFloorSampleIndex + 1) % _recentNoiseFloorSamples.Length;
            
            // Calculate stable noise floor from recent samples
            double noiseFloorSum = 0.0;
            int validSamples = 0;
            for (int i = 0; i < _recentNoiseFloorSamples.Length; i++)
            {
                if (_recentNoiseFloorSamples[i] > 0)
                {
                    noiseFloorSum += _recentNoiseFloorSamples[i];
                    validSamples++;
                }
            }
            
            double adaptiveFloor = validSamples > 0 ? noiseFloorSum / validSamples : 0.001;
            return Math.Max(_config.NoiseFloor, adaptiveFloor * 2.5); // 2.5x average as threshold
        }
        
        private void ApplyMovingAverage()
        {
            if (_movingAverageBuffers == null) return;
            
            for (int i = 0; i < _config.FrequencyBands; i++)
            {
                _movingAverageBuffers[i].Add(_currentSpectrum![i]);
                _currentSpectrum[i] = _movingAverageBuffers[i].Average();
            }
        }
        
        private void CheckForSilenceReset(double rmsLevel)
        {
            const double silenceThreshold = 0.001;
            
            if (rmsLevel < silenceThreshold)
            {
                // Immediately start decaying values during silence
                for (int i = 0; i < _currentSpectrum!.Length; i++)
                {
                    _currentSpectrum[i] *= 0.9; // Decay by 10% each frame during silence
                    _previousSpectrum![i] *= 0.9;
                    
                    if (_currentSpectrum[i] < 0.0001)
                    {
                        _currentSpectrum[i] = 0;
                        _previousSpectrum[i] = 0;
                    }
                }
                
                // Clear buffers after 1 second of silence
                if (_lastSilenceDetection == DateTime.MinValue)
                {
                    _lastSilenceDetection = DateTime.UtcNow;
                }
                else if ((DateTime.UtcNow - _lastSilenceDetection).TotalMilliseconds > 1000)
                {
                    Array.Clear(_previousSpectrum!);
                    Array.Clear(_currentSpectrum!);
                    Array.Clear(_smoothingBuffer!);
                    _lastSilenceDetection = DateTime.MinValue;
                }
            }
            else
            {
                _lastSilenceDetection = DateTime.MinValue;
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
        
        /// <summary>
        /// Simple circular buffer for double values used in spectrum smoothing.
        /// </summary>
        private class CircularBuffer<T>
        {
            private readonly T[] _buffer;
            private readonly int _capacity;
            private int _head = 0;
            private int _count = 0;

            public CircularBuffer(int capacity)
            {
                _capacity = capacity;
                _buffer = new T[capacity];
            }

            public void Add(T item)
            {
                _buffer[_head] = item;
                _head = (_head + 1) % _capacity;
                
                if (_count < _capacity)
                    _count++;
            }

            public double Average()
            {
                if (_count == 0) return 0.0;
                
                double sum = 0.0;
                for (int i = 0; i < _count; i++)
                {
                    sum += Convert.ToDouble(_buffer[i]);
                }
                return sum / _count;
            }

            public void Clear()
            {
                _head = 0;
                _count = 0;
                Array.Clear(_buffer, 0, _capacity);
            }
        }

        #endregion
    }
}