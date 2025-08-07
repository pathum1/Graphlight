using System;
using System.Threading;
using System.Threading.Tasks;

namespace TaskbarEqualizer.Core.Interfaces
{
    /// <summary>
    /// Interface for real-time frequency analysis using FFT processing
    /// with configurable frequency bands and smoothing algorithms.
    /// </summary>
    public interface IFrequencyAnalyzer : IDisposable
    {
        /// <summary>
        /// Event fired when frequency analysis is complete and new spectrum data is available.
        /// </summary>
        event EventHandler<SpectrumDataEventArgs> SpectrumDataAvailable;

        /// <summary>
        /// Gets the current number of frequency bands being analyzed.
        /// </summary>
        int FrequencyBands { get; }

        /// <summary>
        /// Gets the FFT size being used for analysis.
        /// </summary>
        int FftSize { get; }

        /// <summary>
        /// Gets the sample rate of the audio being analyzed.
        /// </summary>
        int SampleRate { get; }

        /// <summary>
        /// Gets a value indicating whether the analyzer is currently processing audio.
        /// </summary>
        bool IsAnalyzing { get; }

        /// <summary>
        /// Configures the frequency analyzer with specified parameters.
        /// </summary>
        /// <param name="fftSize">Size of the FFT (must be power of 2, recommended: 1024).</param>
        /// <param name="sampleRate">Sample rate of input audio (e.g., 44100).</param>
        /// <param name="frequencyBands">Number of frequency bands to output (8-32 recommended).</param>
        /// <param name="smoothingFactor">Smoothing factor for temporal stability (0.0-1.0).</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous configuration operation.</returns>
        Task ConfigureAsync(int fftSize, int sampleRate, int frequencyBands, double smoothingFactor, CancellationToken cancellationToken = default);

        /// <summary>
        /// Processes audio samples and generates frequency spectrum data.
        /// This method should be called from the audio processing thread for optimal performance.
        /// </summary>
        /// <param name="samples">Audio samples to analyze (will be copied internally).</param>
        /// <param name="sampleCount">Number of valid samples in the array.</param>
        /// <param name="timestampTicks">High-resolution timestamp for the audio data.</param>
        /// <returns>Task representing the asynchronous analysis operation.</returns>
        Task ProcessAudioSamplesAsync(float[] samples, int sampleCount, long timestampTicks);

        /// <summary>
        /// Starts the frequency analyzer processing.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous start operation.</returns>
        Task StartAnalysisAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops the frequency analyzer processing and releases resources.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous stop operation.</returns>
        Task StopAnalysisAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the smoothing configuration without stopping analysis.
        /// </summary>
        /// <param name="smoothingFactor">New smoothing factor (0.0-1.0).</param>
        /// <param name="attackTime">Attack time in milliseconds for level increases.</param>
        /// <param name="decayTime">Decay time in milliseconds for level decreases.</param>
        void UpdateSmoothing(double smoothingFactor, double attackTime = 10.0, double decayTime = 100.0);

        /// <summary>
        /// Updates the frequency band configuration without stopping analysis.
        /// </summary>
        /// <param name="frequencyBands">New number of frequency bands.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous reconfiguration.</returns>
        Task UpdateFrequencyBandsAsync(int frequencyBands, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Event arguments for spectrum data availability.
    /// </summary>
    public class SpectrumDataEventArgs : EventArgs
    {
        /// <summary>
        /// Frequency spectrum data organized by bands.
        /// Values are normalized between 0.0 and 1.0.
        /// This array is pooled and will be reused - copy data if needed beyond the event handler.
        /// </summary>
        public double[] Spectrum { get; }

        /// <summary>
        /// Number of valid spectrum bands in the array.
        /// </summary>
        public int BandCount { get; }

        /// <summary>
        /// Peak frequency value in this spectrum analysis.
        /// </summary>
        public double PeakValue { get; }

        /// <summary>
        /// Index of the frequency band with the peak value.
        /// </summary>
        public int PeakBandIndex { get; }

        /// <summary>
        /// Timestamp when the original audio data was captured.
        /// </summary>
        public long TimestampTicks { get; }

        /// <summary>
        /// Processing latency from audio capture to spectrum analysis completion.
        /// </summary>
        public TimeSpan ProcessingLatency { get; }

        /// <summary>
        /// RMS (Root Mean Square) level of the audio data.
        /// </summary>
        public double RmsLevel { get; }

        public SpectrumDataEventArgs(double[] spectrum, int bandCount, double peakValue, int peakBandIndex, 
            long timestampTicks, TimeSpan processingLatency, double rmsLevel)
        {
            Spectrum = spectrum;
            BandCount = bandCount;
            PeakValue = peakValue;
            PeakBandIndex = peakBandIndex;
            TimestampTicks = timestampTicks;
            ProcessingLatency = processingLatency;
            RmsLevel = rmsLevel;
        }
    }

    /// <summary>
    /// Configuration for frequency analysis parameters.
    /// </summary>
    public class FrequencyAnalysisConfiguration
    {
        /// <summary>
        /// FFT size for frequency analysis (must be power of 2).
        /// Recommended: 1024 for balance of resolution and latency.
        /// </summary>
        public int FftSize { get; set; } = 1024;

        /// <summary>
        /// Number of frequency bands to output (8-32 recommended).
        /// </summary>
        public int FrequencyBands { get; set; } = 16;

        /// <summary>
        /// Smoothing factor for temporal stability (0.0-1.0).
        /// Higher values = more smoothing but slower response.
        /// </summary>
        public double SmoothingFactor { get; set; } = 0.8;

        /// <summary>
        /// Attack time in milliseconds for level increases.
        /// Shorter values make the analyzer more responsive to sudden increases.
        /// </summary>
        public double AttackTime { get; set; } = 10.0;

        /// <summary>
        /// Decay time in milliseconds for level decreases.
        /// Longer values make the visualization appear more stable.
        /// </summary>
        public double DecayTime { get; set; } = 100.0;

        /// <summary>
        /// Minimum frequency to analyze in Hz.
        /// </summary>
        public double MinFrequency { get; set; } = 20.0;

        /// <summary>
        /// Maximum frequency to analyze in Hz.
        /// </summary>
        public double MaxFrequency { get; set; } = 20000.0;

        /// <summary>
        /// Use logarithmic frequency scaling (recommended for music visualization).
        /// </summary>
        public bool UseLogarithmicScaling { get; set; } = true;

        /// <summary>
        /// Apply window function to reduce spectral leakage.
        /// </summary>
        public WindowFunction WindowFunction { get; set; } = WindowFunction.Hann;
        
        /// <summary>
        /// Noise floor threshold in linear scale (0.001 = -60dB).
        /// Values below this threshold are treated as silence.
        /// </summary>
        public double NoiseFloor { get; set; } = 0.001;
        
        /// <summary>
        /// Enable adaptive noise floor calculation based on ambient noise levels.
        /// </summary>
        public bool UseAdaptiveNoiseFloor { get; set; } = true;
        
        /// <summary>
        /// Moving average size for stability (1-10). 
        /// Higher values provide more stable output but slower response.
        /// </summary>
        public int MovingAverageSize { get; set; } = 3;
        
        /// <summary>
        /// Silence reset timeout in milliseconds.
        /// After this period of silence, spectrum buffers are reset to prevent accumulation.
        /// </summary>
        public double SilenceResetTimeoutMs { get; set; } = 2000.0;
    }

    /// <summary>
    /// Window functions for FFT preprocessing.
    /// </summary>
    public enum WindowFunction
    {
        /// <summary>
        /// No windowing (rectangular window).
        /// </summary>
        None,

        /// <summary>
        /// Hann window (recommended for general use).
        /// </summary>
        Hann,

        /// <summary>
        /// Hamming window.
        /// </summary>
        Hamming,

        /// <summary>
        /// Blackman window.
        /// </summary>
        Blackman,

        /// <summary>
        /// Kaiser window.
        /// </summary>
        Kaiser
    }
}