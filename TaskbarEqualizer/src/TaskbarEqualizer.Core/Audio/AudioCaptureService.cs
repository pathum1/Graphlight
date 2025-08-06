using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using TaskbarEqualizer.Core.Interfaces;
using TaskbarEqualizer.Core.DataStructures;

namespace TaskbarEqualizer.Core.Audio
{
    /// <summary>
    /// High-performance audio capture service using WASAPI loopback for system audio monitoring.
    /// Optimized for real-time processing with minimal latency and resource usage.
    /// </summary>
    public sealed class AudioCaptureService : IAudioCaptureService
    {
        private readonly ILogger<AudioCaptureService> _logger;
        private readonly AudioSamplePool _samplePool;
        private readonly MMDeviceEnumerator _deviceEnumerator;
        
        private WasapiLoopbackCapture? _capture;
        private MMDevice? _currentDevice;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _captureTask;
        
        private volatile bool _isCapturing;
        private volatile bool _disposed;
        
        // Performance optimization fields
        private readonly object _stateLock = new object();
        private int _bufferSize = 1024;
        private long _lastTimestamp;

        /// <summary>
        /// Initializes a new instance of the AudioCaptureService.
        /// </summary>
        /// <param name="logger">Logger for diagnostic information.</param>
        public AudioCaptureService(ILogger<AudioCaptureService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _deviceEnumerator = new MMDeviceEnumerator();
            // Use a buffer size that can handle larger audio capture scenarios
            // Calculate max expected buffer size: NextPowerOfTwo of 44100 * 23ms = 2048
            int maxExpectedBufferSize = 2048; // Power of 2, handles up to ~46ms at 44.1kHz
            _samplePool = PoolFactory.CreateAudioSamplePoolForFft(maxExpectedBufferSize);
            _lastTimestamp = Environment.TickCount64;
            
            _logger.LogDebug("AudioCaptureService initialized");
        }

        #region Events

        /// <inheritdoc />
        public event EventHandler<AudioDataEventArgs>? AudioDataAvailable;

        /// <inheritdoc />
        public event EventHandler<AudioDeviceChangedEventArgs>? AudioDeviceChanged;

        #endregion

        #region Properties

        /// <inheritdoc />
        public MMDevice? CurrentDevice => _currentDevice;

        /// <inheritdoc />
        public bool IsCapturing => _isCapturing;

        /// <inheritdoc />
        public WaveFormat? AudioFormat => _capture?.WaveFormat;

        /// <inheritdoc />
        public int BufferSize => _bufferSize;

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public async Task StartCaptureAsync(CancellationToken cancellationToken = default)
        {
            var defaultDevice = GetDefaultDevice();
            if (defaultDevice == null)
            {
                throw new InvalidOperationException("No default audio device available");
            }

            await StartCaptureAsync(defaultDevice, cancellationToken);
        }

        /// <inheritdoc />
        public async Task StartCaptureAsync(MMDevice device, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioCaptureService));

            if (device == null)
                throw new ArgumentNullException(nameof(device));

            lock (_stateLock)
            {
                if (_isCapturing)
                {
                    _logger.LogWarning("Audio capture is already running");
                    return;
                }

                try
                {
                    _logger.LogInformation("Starting audio capture on device: {DeviceName}", device.FriendlyName);

                    // Initialize WASAPI loopback capture
                    _capture = new WasapiLoopbackCapture(device);
                    _currentDevice = device;
                    
                    // Calculate optimal buffer size based on device capabilities
                    CalculateOptimalBufferSize(_capture.WaveFormat);
                    
                    // Set up event handlers
                    _capture.DataAvailable += OnDataAvailable;
                    _capture.RecordingStopped += OnRecordingStopped;

                    // Create cancellation token for this capture session
                    _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                    // Start capture in a separate task for better error handling
                    _captureTask = Task.Run(() => StartCaptureInternal(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

                    _isCapturing = true;
                    _logger.LogInformation("Audio capture started successfully. Format: {Format}, Buffer: {BufferSize}", 
                        _capture.WaveFormat, _bufferSize);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start audio capture");
                    CleanupCapture();
                    throw;
                }
            }

            // Wait for capture to actually start
            await Task.Delay(50, cancellationToken);
        }

        /// <inheritdoc />
        public async Task StopCaptureAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return;

            lock (_stateLock)
            {
                if (!_isCapturing)
                {
                    _logger.LogDebug("Audio capture is not running");
                    return;
                }

                _logger.LogInformation("Stopping audio capture");

                try
                {
                    // Signal cancellation
                    _cancellationTokenSource?.Cancel();

                    // Stop the capture
                    _capture?.StopRecording();
                    _isCapturing = false;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error occurred while stopping audio capture");
                }
            }

            // Wait for capture task to complete
            if (_captureTask != null)
            {
                try
                {
                    await _captureTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Capture task did not complete within timeout");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error waiting for capture task completion");
                }
            }

            CleanupCapture();
            _logger.LogInformation("Audio capture stopped");
        }

        /// <inheritdoc />
        public async Task SwitchDeviceAsync(MMDevice device, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioCaptureService));

            if (device == null)
                throw new ArgumentNullException(nameof(device));

            _logger.LogInformation("Switching audio device from {OldDevice} to {NewDevice}", 
                _currentDevice?.FriendlyName ?? "None", device.FriendlyName);

            var previousDevice = _currentDevice;
            var wasCapturing = _isCapturing;

            try
            {
                // Stop current capture if running
                if (wasCapturing)
                {
                    await StopCaptureAsync(cancellationToken);
                }

                // Start capture on new device
                await StartCaptureAsync(device, cancellationToken);

                // Notify about device change
                AudioDeviceChanged?.Invoke(this, new AudioDeviceChangedEventArgs(
                    previousDevice, device, AudioDeviceChangeReason.UserRequested));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to switch audio device");
                
                // Attempt to restore previous device
                if (previousDevice != null && wasCapturing)
                {
                    try
                    {
                        await StartCaptureAsync(previousDevice, cancellationToken);
                        _logger.LogInformation("Restored previous audio device after switch failure");
                    }
                    catch (Exception restoreEx)
                    {
                        _logger.LogError(restoreEx, "Failed to restore previous audio device");
                    }
                }
                
                throw;
            }
        }

        /// <inheritdoc />
        public MMDevice[] GetAvailableDevices()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioCaptureService));

            try
            {
                var devices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                var deviceArray = new MMDevice[devices.Count];
                
                for (int i = 0; i < devices.Count; i++)
                {
                    deviceArray[i] = devices[i];
                }
                
                _logger.LogDebug("Found {DeviceCount} available audio devices", deviceArray.Length);
                return deviceArray;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enumerating audio devices");
                return Array.Empty<MMDevice>();
            }
        }

        #endregion

        #region Private Methods

        private MMDevice? GetDefaultDevice()
        {
            try
            {
                return _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get default audio device");
                return null;
            }
        }

        private void CalculateOptimalBufferSize(WaveFormat format)
        {
            // Calculate buffer size for approximately 23ms latency (1024 samples at 44.1kHz)
            var samplesPerMs = format.SampleRate / 1000.0;
            var targetLatencyMs = 23.0;
            var calculatedSize = (int)Math.Ceiling(samplesPerMs * targetLatencyMs);
            
            // Round to next power of 2 for better memory alignment
            _bufferSize = NextPowerOfTwo(calculatedSize);
            
            _logger.LogDebug("Calculated buffer size: {BufferSize} samples for {LatencyMs}ms latency", 
                _bufferSize, targetLatencyMs);
        }

        private static int NextPowerOfTwo(int value)
        {
            if (value <= 0) return 1;
            
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value++;
            
            return value;
        }

        private void StartCaptureInternal(CancellationToken cancellationToken)
        {
            try
            {
                _capture?.StartRecording();
                _logger.LogDebug("WASAPI capture started successfully");

                // Keep the capture alive until cancellation
                while (!cancellationToken.IsCancellationRequested && _isCapturing)
                {
                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in capture thread");
                _isCapturing = false;
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_disposed || !_isCapturing || e.BytesRecorded == 0)
                return;

            try
            {
                var timestamp = Environment.TickCount64;
                var format = _capture?.WaveFormat;
                
                if (format == null)
                    return;

                // Convert bytes to float samples
                var sampleCount = e.BytesRecorded / (format.BitsPerSample / 8);
                var samples = _samplePool.Get();
                
                try
                {
                    // Ensure we have enough space in the pooled array
                    if (samples.Length < sampleCount)
                    {
                        _samplePool.Return(samples);
                        samples = new float[sampleCount];
                    }

                    // Convert based on bit depth
                    if (format.BitsPerSample == 16)
                    {
                        ConvertInt16ToFloat(e.Buffer, samples, sampleCount);
                    }
                    else if (format.BitsPerSample == 24)
                    {
                        ConvertInt24ToFloat(e.Buffer, samples, sampleCount);
                    }
                    else if (format.BitsPerSample == 32)
                    {
                        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
                        {
                            ConvertFloat32ToFloat(e.Buffer, samples, sampleCount);
                        }
                        else
                        {
                            ConvertInt32ToFloat(e.Buffer, samples, sampleCount);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Unsupported audio format: {BitsPerSample} bits", format.BitsPerSample);
                        return;
                    }

                    // Fire event with converted samples
                    AudioDataAvailable?.Invoke(this, new AudioDataEventArgs(samples, sampleCount, format, timestamp));
                }
                finally
                {
                    // Return array to pool if it's the correct size
                    if (samples.Length == _samplePool.SampleSize)
                    {
                        _samplePool.Return(samples);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio data");
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _logger.LogDebug("Recording stopped. Exception: {Exception}", e.Exception?.Message ?? "None");
            
            if (e.Exception != null)
            {
                _logger.LogError(e.Exception, "Audio recording stopped due to error");
                
                // Notify about device becoming unavailable
                AudioDeviceChanged?.Invoke(this, new AudioDeviceChangedEventArgs(
                    _currentDevice, null, AudioDeviceChangeReason.DeviceUnavailable));
            }
            
            _isCapturing = false;
        }

        private void CleanupCapture()
        {
            try
            {
                if (_capture != null)
                {
                    _capture.DataAvailable -= OnDataAvailable;
                    _capture.RecordingStopped -= OnRecordingStopped;
                    _capture.Dispose();
                    _capture = null;
                }

                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _captureTask = null;
                _currentDevice = null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during capture cleanup");
            }
        }

        #endregion

        #region Audio Format Conversion Methods

        private static unsafe void ConvertInt16ToFloat(byte[] buffer, float[] samples, int sampleCount)
        {
            fixed (byte* bufferPtr = buffer)
            fixed (float* samplesPtr = samples)
            {
                short* int16Ptr = (short*)bufferPtr;
                for (int i = 0; i < sampleCount; i++)
                {
                    samplesPtr[i] = int16Ptr[i] / 32768.0f;
                }
            }
        }

        private static unsafe void ConvertInt24ToFloat(byte[] buffer, float[] samples, int sampleCount)
        {
            fixed (byte* bufferPtr = buffer)
            fixed (float* samplesPtr = samples)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    // 24-bit samples are 3 bytes each
                    int byteIndex = i * 3;
                    int sample24 = (bufferPtr[byteIndex] << 8) | (bufferPtr[byteIndex + 1] << 16) | (bufferPtr[byteIndex + 2] << 24);
                    samplesPtr[i] = sample24 / 2147483648.0f; // Divide by 2^31
                }
            }
        }

        private static unsafe void ConvertInt32ToFloat(byte[] buffer, float[] samples, int sampleCount)
        {
            fixed (byte* bufferPtr = buffer)
            fixed (float* samplesPtr = samples)
            {
                int* int32Ptr = (int*)bufferPtr;
                for (int i = 0; i < sampleCount; i++)
                {
                    samplesPtr[i] = int32Ptr[i] / 2147483648.0f; // Divide by 2^31
                }
            }
        }

        private static unsafe void ConvertFloat32ToFloat(byte[] buffer, float[] samples, int sampleCount)
        {
            fixed (byte* bufferPtr = buffer)
            fixed (float* samplesPtr = samples)
            {
                float* floatPtr = (float*)bufferPtr;
                for (int i = 0; i < sampleCount; i++)
                {
                    samplesPtr[i] = floatPtr[i];
                }
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the audio capture service and releases all resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                
                try
                {
                    // Stop capture synchronously during disposal
                    if (_isCapturing)
                    {
                        StopCaptureAsync().Wait(TimeSpan.FromSeconds(2));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping capture during disposal");
                }

                CleanupCapture();
                _samplePool?.Dispose();
                _deviceEnumerator?.Dispose();
                
                _logger.LogDebug("AudioCaptureService disposed");
            }
        }

        #endregion
    }
}