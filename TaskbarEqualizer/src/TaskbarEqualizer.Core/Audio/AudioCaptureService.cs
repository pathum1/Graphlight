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
        
        // Circuit breaker for error recovery
        private int _consecutiveErrors = 0;
        private DateTime _lastErrorTime = DateTime.MinValue;
        private const int MAX_CONSECUTIVE_ERRORS = 10;
        private static readonly TimeSpan ERROR_RESET_INTERVAL = TimeSpan.FromSeconds(30);
        
        // Audio level monitoring
        private DateTime _lastAudioLevelLog = DateTime.MinValue;
        private float _maxPeakSinceLastLog = 0f;
        private float _maxRmsSinceLastLog = 0f;
        private static readonly TimeSpan AUDIO_LEVEL_LOG_INTERVAL = TimeSpan.FromSeconds(5);

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
                    _logger.LogInformation("Starting WASAPI loopback capture on device: {DeviceName} [{DeviceId}]", 
                        device.FriendlyName, device.ID);

                    // Log device properties for debugging
                    try
                    {
                        _logger.LogDebug("Device state: {State}, DataFlow: {DataFlow}", 
                            device.State, device.DataFlow);
                        
                        // Check if device is the current default multimedia device
                        var defaultMultimedia = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        var defaultConsole = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                        
                        _logger.LogDebug("Device is default multimedia: {IsDefaultMM}, default console: {IsDefaultConsole}",
                            device.ID == defaultMultimedia?.ID,
                            device.ID == defaultConsole?.ID);
                    }
                    catch (Exception logEx)
                    {
                        _logger.LogWarning(logEx, "Failed to log device properties");
                    }

                    // Initialize WASAPI loopback capture
                    _capture = new WasapiLoopbackCapture(device);
                    _currentDevice = device;
                    
                    // Log the audio format we'll be capturing
                    _logger.LogInformation("Audio format for loopback capture: {Format}", _capture.WaveFormat);
                    
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
                
                _logger.LogDebug("Found {DeviceCount} available render devices for loopback capture:", deviceArray.Length);
                
                // Log each available device for debugging
                for (int i = 0; i < deviceArray.Length; i++)
                {
                    var device = deviceArray[i];
                    try
                    {
                        _logger.LogDebug("  Device {Index}: {Name} [{Id}] - State: {State}", 
                            i, device.FriendlyName, device.ID, device.State);
                    }
                    catch (Exception deviceEx)
                    {
                        _logger.LogWarning(deviceEx, "  Device {Index}: Error getting device info", i);
                    }
                }
                
                return deviceArray;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enumerating audio render devices");
                return Array.Empty<MMDevice>();
            }
        }

        /// <summary>
        /// Attempts to find and start loopback capture on the best available device for audio visualization.
        /// This method tries multiple strategies to ensure we capture actual system audio playback.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        public async Task StartBestLoopbackCaptureAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioCaptureService));

            _logger.LogInformation("Attempting to find best device for WASAPI loopback capture");

            // Strategy 1: Try default multimedia device
            try
            {
                var multimediaDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _logger.LogDebug("Trying default multimedia device: {DeviceName}", multimediaDevice.FriendlyName);
                
                await StartCaptureAsync(multimediaDevice, cancellationToken);
                _logger.LogInformation("Successfully started loopback capture on multimedia device: {DeviceName}", multimediaDevice.FriendlyName);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to start capture on default multimedia device");
            }

            // Strategy 2: Try default console device
            try
            {
                var consoleDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                _logger.LogDebug("Trying default console device: {DeviceName}", consoleDevice.FriendlyName);
                
                await StartCaptureAsync(consoleDevice, cancellationToken);
                _logger.LogInformation("Successfully started loopback capture on console device: {DeviceName}", consoleDevice.FriendlyName);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to start capture on default console device");
            }

            // Strategy 3: Try all available active render devices
            var devices = GetAvailableDevices();
            for (int i = 0; i < devices.Length; i++)
            {
                try
                {
                    var device = devices[i];
                    _logger.LogDebug("Trying device {Index}: {DeviceName}", i, device.FriendlyName);
                    
                    await StartCaptureAsync(device, cancellationToken);
                    _logger.LogInformation("Successfully started loopback capture on device: {DeviceName}", device.FriendlyName);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to start capture on device {Index}: {DeviceName}", 
                        i, devices[i].FriendlyName);
                }
            }

            throw new InvalidOperationException("Failed to start WASAPI loopback capture on any available render device");
        }

        #endregion

        #region Private Methods

        private MMDevice? GetDefaultDevice()
        {
            try
            {
                // Log all available render devices for debugging
                LogAvailableRenderDevices();
                
                // For audio visualization, we want the multimedia playback device (what plays music/videos)
                // Try multimedia role first, then console as fallback
                try
                {
                    var multimediaDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    _logger.LogDebug("Using multimedia render device: {DeviceName}", multimediaDevice.FriendlyName);
                    return multimediaDevice;
                }
                catch (Exception mmEx)
                {
                    _logger.LogWarning(mmEx, "Failed to get multimedia render device, trying console role");
                    
                    var consoleDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                    _logger.LogDebug("Using console render device: {DeviceName}", consoleDevice.FriendlyName);
                    return consoleDevice;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get any default audio render device");
                
                // Last resort: try to get any active render device
                try
                {
                    var devices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                    if (devices.Count > 0)
                    {
                        var fallbackDevice = devices[0];
                        _logger.LogWarning("Using fallback render device: {DeviceName}", fallbackDevice.FriendlyName);
                        return fallbackDevice;
                    }
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "Failed to get fallback render device");
                }
                
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

            // Circuit breaker: skip processing if too many consecutive errors
            if (_consecutiveErrors >= MAX_CONSECUTIVE_ERRORS)
            {
                if (DateTime.Now - _lastErrorTime < ERROR_RESET_INTERVAL)
                {
                    return; // Skip processing during cooldown period
                }
                else
                {
                    // Reset error counter after cooldown
                    _consecutiveErrors = 0;
                    _logger.LogInformation("Audio processing error circuit breaker reset");
                }
            }

            try
            {
                var timestamp = Environment.TickCount64;
                var format = _capture?.WaveFormat;
                
                if (format == null)
                    return;

                // Convert bytes to float samples - account for stereo (2 channels)
                var bytesPerSample = format.BitsPerSample / 8;
                var totalSamples = e.BytesRecorded / bytesPerSample;
                var sampleCount = format.Channels == 2 ? totalSamples / 2 : totalSamples; // Mono output for FFT
                var samples = _samplePool.Get();
                
                // Debug logging for first few data packets to verify we're getting audio
                if (DateTime.Now.Millisecond % 1000 < 50) // Log roughly once per second
                {
                    _logger.LogDebug("WASAPI loopback data: {BytesRecorded} bytes, {TotalSamples} samples, {SampleCount} output samples, Format: {Format}", 
                        e.BytesRecorded, totalSamples, sampleCount, format);
                }
                
                try
                {
                    // Ensure we have enough space in the pooled array
                    if (samples.Length < sampleCount)
                    {
                        _samplePool.Return(samples);
                        samples = new float[sampleCount];
                    }

                    // Convert based on bit depth with stereo-to-mono conversion
                    if (format.BitsPerSample == 16)
                    {
                        ConvertInt16ToFloat(e.Buffer, samples, sampleCount, format.Channels);
                    }
                    else if (format.BitsPerSample == 24)
                    {
                        ConvertInt24ToFloat(e.Buffer, samples, sampleCount, format.Channels);
                    }
                    else if (format.BitsPerSample == 32)
                    {
                        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
                        {
                            ConvertFloat32ToFloat(e.Buffer, samples, sampleCount, format.Channels);
                        }
                        else
                        {
                            ConvertInt32ToFloat(e.Buffer, samples, sampleCount, format.Channels);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Unsupported audio format: {BitsPerSample} bits", format.BitsPerSample);
                        return;
                    }

                    // Calculate RMS for debugging - check if we're getting actual audio
                    double rms = 0.0;
                    for (int i = 0; i < sampleCount; i++)
                    {
                        rms += samples[i] * samples[i];
                    }
                    rms = Math.Sqrt(rms / sampleCount);
                    
                    // Find peak value for debugging
                    float peak = 0.0f;
                    for (int i = 0; i < sampleCount; i++)
                    {
                        float abs = Math.Abs(samples[i]);
                        if (abs > peak) peak = abs;
                    }
                    
                    // Log audio levels occasionally for debugging
                    if (DateTime.Now.Millisecond % 2000 < 50) // Log every 2 seconds
                    {
                        _logger.LogDebug("Audio levels - RMS: {Rms:F6}, Peak: {Peak:F6}, Device: {DeviceName}", 
                            rms, peak, _currentDevice?.FriendlyName ?? "Unknown");
                        
                        if (rms < 0.000001 && peak < 0.000001)
                        {
                            _logger.LogWarning("Very low audio levels detected - may not be capturing system audio playback correctly");
                        }
                    }

                    // Monitor audio levels for debugging
                    MonitorAudioLevels(samples, sampleCount);
                    
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
                _consecutiveErrors++;
                _lastErrorTime = DateTime.Now;
                
                _logger.LogError(ex, "Error processing audio data (consecutive errors: {Count})", _consecutiveErrors);
                
                if (_consecutiveErrors >= MAX_CONSECUTIVE_ERRORS)
                {
                    _logger.LogWarning("Audio processing circuit breaker triggered due to {Count} consecutive errors", _consecutiveErrors);
                }
            }
            
            // Reset error counter on success
            if (_consecutiveErrors > 0)
            {
                _consecutiveErrors = 0;
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

        private static unsafe void ConvertInt16ToFloat(byte[] buffer, float[] samples, int sampleCount, int channels)
        {
            fixed (byte* bufferPtr = buffer)
            fixed (float* samplesPtr = samples)
            {
                short* int16Ptr = (short*)bufferPtr;
                if (channels == 2)
                {
                    // Stereo to mono: average left and right channels
                    for (int i = 0; i < sampleCount; i++)
                    {
                        samplesPtr[i] = (int16Ptr[i * 2] + int16Ptr[i * 2 + 1]) / 65536.0f;
                    }
                }
                else
                {
                    // Mono or other: direct conversion
                    for (int i = 0; i < sampleCount; i++)
                    {
                        samplesPtr[i] = int16Ptr[i] / 32768.0f;
                    }
                }
            }
        }

        private static unsafe void ConvertInt24ToFloat(byte[] buffer, float[] samples, int sampleCount, int channels)
        {
            fixed (byte* bufferPtr = buffer)
            fixed (float* samplesPtr = samples)
            {
                if (channels == 2)
                {
                    // Stereo to mono: average left and right channels
                    for (int i = 0; i < sampleCount; i++)
                    {
                        // 24-bit samples are 3 bytes each, 2 channels = 6 bytes per sample pair
                        int leftIndex = i * 6;
                        int rightIndex = leftIndex + 3;
                        
                        int leftSample = (bufferPtr[leftIndex] << 8) | (bufferPtr[leftIndex + 1] << 16) | (bufferPtr[leftIndex + 2] << 24);
                        int rightSample = (bufferPtr[rightIndex] << 8) | (bufferPtr[rightIndex + 1] << 16) | (bufferPtr[rightIndex + 2] << 24);
                        
                        samplesPtr[i] = ((leftSample + rightSample) * 0.5f) / 2147483648.0f;
                    }
                }
                else
                {
                    // Mono or other: direct conversion
                    for (int i = 0; i < sampleCount; i++)
                    {
                        // 24-bit samples are 3 bytes each
                        int byteIndex = i * 3;
                        int sample24 = (bufferPtr[byteIndex] << 8) | (bufferPtr[byteIndex + 1] << 16) | (bufferPtr[byteIndex + 2] << 24);
                        samplesPtr[i] = sample24 / 2147483648.0f; // Divide by 2^31
                    }
                }
            }
        }

        private static unsafe void ConvertInt32ToFloat(byte[] buffer, float[] samples, int sampleCount, int channels)
        {
            fixed (byte* bufferPtr = buffer)
            fixed (float* samplesPtr = samples)
            {
                int* int32Ptr = (int*)bufferPtr;
                if (channels == 2)
                {
                    // Stereo to mono: average left and right channels
                    for (int i = 0; i < sampleCount; i++)
                    {
                        samplesPtr[i] = ((int32Ptr[i * 2] + int32Ptr[i * 2 + 1]) * 0.5f) / 2147483648.0f;
                    }
                }
                else
                {
                    // Mono or other: direct conversion
                    for (int i = 0; i < sampleCount; i++)
                    {
                        samplesPtr[i] = int32Ptr[i] / 2147483648.0f; // Divide by 2^31
                    }
                }
            }
        }

        private static unsafe void ConvertFloat32ToFloat(byte[] buffer, float[] samples, int sampleCount, int channels)
        {
            fixed (byte* bufferPtr = buffer)
            fixed (float* samplesPtr = samples)
            {
                float* floatPtr = (float*)bufferPtr;
                if (channels == 2)
                {
                    // Stereo to mono: average left and right channels
                    for (int i = 0; i < sampleCount; i++)
                    {
                        samplesPtr[i] = (floatPtr[i * 2] + floatPtr[i * 2 + 1]) * 0.5f;
                    }
                }
                else
                {
                    // Mono or other: direct copy
                    for (int i = 0; i < sampleCount; i++)
                    {
                        samplesPtr[i] = floatPtr[i];
                    }
                }
            }
        }

        /// <summary>
        /// Logs all available render devices for debugging device selection issues.
        /// </summary>
        private void LogAvailableRenderDevices()
        {
            try
            {
                _logger.LogInformation("=== Available Audio Render Devices ===");
                var devices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                
                for (int i = 0; i < devices.Count; i++)
                {
                    var device = devices[i];
                    try
                    {
                        // Check which roles this device serves
                        bool isDefaultMultimedia = false;
                        bool isDefaultConsole = false;
                        
                        try
                        {
                            var defaultMM = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                            isDefaultMultimedia = device.ID == defaultMM?.ID;
                        }
                        catch { }
                        
                        try
                        {
                            var defaultConsole = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                            isDefaultConsole = device.ID == defaultConsole?.ID;
                        }
                        catch { }
                        
                        _logger.LogInformation("  [{Index}] {Name} - State: {State}, DefaultMM: {IsDefaultMM}, DefaultConsole: {IsDefaultConsole}",
                            i, device.FriendlyName, device.State, isDefaultMultimedia, isDefaultConsole);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to get properties for device {Index}: {Error}", i, ex.Message);
                    }
                }
                _logger.LogInformation("=== End Device List ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enumerate audio devices");
            }
        }

        /// <summary>
        /// Monitors audio levels and logs warnings if levels are too low for visualization.
        /// </summary>
        /// <param name="samples">Audio samples to analyze</param>
        /// <param name="sampleCount">Number of samples</param>
        private void MonitorAudioLevels(float[] samples, int sampleCount)
        {
            // Calculate peak and RMS levels for this buffer
            float peak = 0f;
            float rmsSum = 0f;
            
            for (int i = 0; i < sampleCount; i++)
            {
                float absValue = Math.Abs(samples[i]);
                peak = Math.Max(peak, absValue);
                rmsSum += samples[i] * samples[i];
            }
            
            float rms = (float)Math.Sqrt(rmsSum / sampleCount);
            
            // Track maximum levels since last log
            _maxPeakSinceLastLog = Math.Max(_maxPeakSinceLastLog, peak);
            _maxRmsSinceLastLog = Math.Max(_maxRmsSinceLastLog, rms);
            
            // Log audio levels periodically
            var now = DateTime.Now;
            if (now - _lastAudioLevelLog >= AUDIO_LEVEL_LOG_INTERVAL)
            {
                _logger.LogInformation("Audio levels - Peak: {Peak:F4}, RMS: {Rms:F4} (max since last: Peak={MaxPeak:F4}, RMS={MaxRms:F4})",
                    peak, rms, _maxPeakSinceLastLog, _maxRmsSinceLastLog);
                
                // Warn if levels are consistently very low
                if (_maxPeakSinceLastLog < 0.001f && _maxRmsSinceLastLog < 0.0005f)
                {
                    _logger.LogWarning("Audio levels are extremely low - check that system audio is playing and device is correct");
                    _logger.LogInformation("Try playing music/video and ensure TaskbarEqualizer has permission to access audio");
                }
                else if (_maxPeakSinceLastLog < 0.01f)
                {
                    _logger.LogDebug("Audio levels are low but detectable - visualization may be subtle");
                }
                
                _lastAudioLevelLog = now;
                _maxPeakSinceLastLog = 0f;
                _maxRmsSinceLastLog = 0f;
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