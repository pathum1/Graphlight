using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace TaskbarEqualizer.Core.Interfaces
{
    /// <summary>
    /// Interface for audio capture service providing real-time system audio capture
    /// with WASAPI loopback functionality and low-latency processing.
    /// </summary>
    public interface IAudioCaptureService : IDisposable
    {
        /// <summary>
        /// Event fired when new audio data is available for processing.
        /// This event is fired from the audio capture thread and should be handled quickly.
        /// </summary>
        event EventHandler<AudioDataEventArgs> AudioDataAvailable;

        /// <summary>
        /// Event fired when the audio capture device changes or becomes unavailable.
        /// </summary>
        event EventHandler<AudioDeviceChangedEventArgs> AudioDeviceChanged;

        /// <summary>
        /// Gets the current audio capture device being used.
        /// </summary>
        MMDevice? CurrentDevice { get; }

        /// <summary>
        /// Gets a value indicating whether audio capture is currently active.
        /// </summary>
        bool IsCapturing { get; }

        /// <summary>
        /// Gets the current audio format being captured.
        /// </summary>
        WaveFormat? AudioFormat { get; }

        /// <summary>
        /// Gets the current buffer size in samples.
        /// </summary>
        int BufferSize { get; }

        /// <summary>
        /// Starts audio capture on the default system audio device.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        Task StartCaptureAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts audio capture on the specified audio device.
        /// </summary>
        /// <param name="device">The audio device to use for capture.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        Task StartCaptureAsync(MMDevice device, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops audio capture and releases resources.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        Task StopCaptureAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Switches to a different audio capture device without stopping capture.
        /// </summary>
        /// <param name="device">The new audio device to use.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        Task SwitchDeviceAsync(MMDevice device, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all available audio capture devices.
        /// </summary>
        /// <returns>Array of available audio devices.</returns>
        MMDevice[] GetAvailableDevices();
    }

    /// <summary>
    /// Event arguments for audio data availability.
    /// </summary>
    public class AudioDataEventArgs : EventArgs
    {
        /// <summary>
        /// Raw audio samples from the capture device.
        /// This array is pooled and will be reused - copy data if needed beyond the event handler.
        /// </summary>
        public float[] Samples { get; }

        /// <summary>
        /// Number of valid samples in the Samples array.
        /// </summary>
        public int SampleCount { get; }

        /// <summary>
        /// Audio format of the captured data.
        /// </summary>
        public WaveFormat Format { get; }

        /// <summary>
        /// Timestamp when the audio data was captured (high-resolution).
        /// </summary>
        public long TimestampTicks { get; }

        public AudioDataEventArgs(float[] samples, int sampleCount, WaveFormat format, long timestampTicks)
        {
            Samples = samples;
            SampleCount = sampleCount;
            Format = format;
            TimestampTicks = timestampTicks;
        }
    }

    /// <summary>
    /// Event arguments for audio device changes.
    /// </summary>
    public class AudioDeviceChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The previous audio device (null if none).
        /// </summary>
        public MMDevice? PreviousDevice { get; }

        /// <summary>
        /// The new audio device (null if device became unavailable).
        /// </summary>
        public MMDevice? NewDevice { get; }

        /// <summary>
        /// Reason for the device change.
        /// </summary>
        public AudioDeviceChangeReason Reason { get; }

        public AudioDeviceChangedEventArgs(MMDevice? previousDevice, MMDevice? newDevice, AudioDeviceChangeReason reason)
        {
            PreviousDevice = previousDevice;
            NewDevice = newDevice;
            Reason = reason;
        }
    }

    /// <summary>
    /// Reasons for audio device changes.
    /// </summary>
    public enum AudioDeviceChangeReason
    {
        /// <summary>
        /// Device was manually switched by user.
        /// </summary>
        UserRequested,

        /// <summary>
        /// Device became unavailable (unplugged, disabled, etc.).
        /// </summary>
        DeviceUnavailable,

        /// <summary>
        /// Default system device changed.
        /// </summary>
        DefaultDeviceChanged,

        /// <summary>
        /// Audio driver or service restarted.
        /// </summary>
        ServiceRestarted
    }
}