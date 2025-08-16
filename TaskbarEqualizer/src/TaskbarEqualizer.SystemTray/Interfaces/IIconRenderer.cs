using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using TaskbarEqualizer.Core.Interfaces;

namespace TaskbarEqualizer.SystemTray.Interfaces
{
    /// <summary>
    /// Interface for high-performance icon rendering with Windows 11 design compliance.
    /// Provides real-time equalizer visualization with 60 FPS target performance.
    /// </summary>
    public interface IIconRenderer : IDisposable
    {
        /// <summary>
        /// Event fired when icon rendering is completed.
        /// </summary>
        event EventHandler<RenderingCompletedEventArgs> RenderingCompleted;

        /// <summary>
        /// Gets the current rendering configuration.
        /// </summary>
        RenderConfiguration Configuration { get; }

        /// <summary>
        /// Gets the current icon size being rendered.
        /// </summary>
        IconSize IconSize { get; }

        /// <summary>
        /// Gets the current rendering metrics for performance monitoring.
        /// </summary>
        RenderingMetrics CurrentMetrics { get; }

        /// <summary>
        /// Initializes the icon renderer with the specified configuration.
        /// </summary>
        /// <param name="configuration">Initial rendering configuration.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous initialization.</returns>
        Task InitializeAsync(RenderConfiguration configuration, CancellationToken cancellationToken = default);

        /// <summary>
        /// Renders an equalizer icon based on the provided spectrum data.
        /// </summary>
        /// <param name="spectrumData">Real-time frequency spectrum data.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task containing the rendered icon.</returns>
        Task<Icon> RenderEqualizerIconAsync(SpectrumDataEventArgs spectrumData, CancellationToken cancellationToken = default);

        /// <summary>
        /// Renders an equalizer icon synchronously for high-performance scenarios.
        /// </summary>
        /// <param name="spectrumData">Real-time frequency spectrum data.</param>
        /// <returns>The rendered icon.</returns>
        Icon RenderEqualizerIcon(SpectrumDataEventArgs spectrumData);

        /// <summary>
        /// Updates the rendering configuration without recreating the renderer.
        /// </summary>
        /// <param name="configuration">New rendering configuration.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous update.</returns>
        Task UpdateConfigurationAsync(RenderConfiguration configuration, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the icon size and optimizes rendering accordingly.
        /// </summary>
        /// <param name="iconSize">New icon size to render.</param>
        void SetIconSize(IconSize iconSize);

        /// <summary>
        /// Applies a new visual theme to the renderer.
        /// </summary>
        /// <param name="theme">Theme configuration to apply.</param>
        void ApplyTheme(VisualizationTheme theme);

        /// <summary>
        /// Clears all cached rendering resources to free memory.
        /// </summary>
        void ClearCache();

        /// <summary>
        /// Gets detailed performance metrics for optimization analysis.
        /// </summary>
        /// <returns>Comprehensive rendering performance metrics.</returns>
        DetailedRenderingMetrics GetDetailedMetrics();
    }

    /// <summary>
    /// Event arguments for rendering completion events.
    /// </summary>
    public class RenderingCompletedEventArgs : EventArgs
    {
        /// <summary>
        /// The rendered icon result.
        /// </summary>
        public Icon RenderedIcon { get; }

        /// <summary>
        /// Time taken to complete the rendering operation.
        /// </summary>
        public TimeSpan RenderingTime { get; }

        /// <summary>
        /// Spectrum data that was used for rendering.
        /// </summary>
        public SpectrumDataEventArgs SpectrumData { get; }

        /// <summary>
        /// Whether the rendering was skipped due to optimization.
        /// </summary>
        public bool WasSkipped { get; }

        /// <summary>
        /// Reason for skipping if applicable.
        /// </summary>
        public string? SkipReason { get; }

        public RenderingCompletedEventArgs(Icon renderedIcon, TimeSpan renderingTime, 
            SpectrumDataEventArgs spectrumData, bool wasSkipped = false, string? skipReason = null)
        {
            RenderedIcon = renderedIcon;
            RenderingTime = renderingTime;
            SpectrumData = spectrumData;
            WasSkipped = wasSkipped;
            SkipReason = skipReason;
        }
    }

    /// <summary>
    /// Configuration for icon rendering operations.
    /// </summary>
    public class RenderConfiguration
    {
        /// <summary>
        /// Target icon size to render.
        /// </summary>
        public IconSize IconSize { get; set; } = IconSize.Medium;

        /// <summary>
        /// Visual style for the equalizer bars.
        /// </summary>
        public EqualizerStyle Style { get; set; } = EqualizerStyle.Bars;

        /// <summary>
        /// Color scheme for the visualization.
        /// </summary>
        public ColorScheme ColorScheme { get; set; } = new();

        /// <summary>
        /// Target frame rate for animations.
        /// </summary>
        public int TargetFrameRate { get; set; } = 60;

        /// <summary>
        /// Quality level for rendering.
        /// </summary>
        public RenderQuality Quality { get; set; } = RenderQuality.High;

        /// <summary>
        /// Whether to enable anti-aliasing.
        /// </summary>
        public bool AntiAliasing { get; set; } = true;

        /// <summary>
        /// Whether to enable visual effects (shadows, gradients).
        /// </summary>
        public bool EnableEffects { get; set; } = true;

        /// <summary>
        /// Whether to enable animations.
        /// </summary>
        public bool EnableAnimations { get; set; } = true;

        /// <summary>
        /// Animation configuration settings.
        /// </summary>
        public AnimationConfiguration Animation { get; set; } = new();

        /// <summary>
        /// Whether to enable adaptive quality based on performance.
        /// </summary>
        public bool AdaptiveQuality { get; set; } = true;

        /// <summary>
        /// Minimum change threshold to trigger re-rendering (0.0-1.0).
        /// </summary>
        public double ChangeThreshold { get; set; } = 0.02;

        /// <summary>
        /// Gets the hash code for this RenderConfiguration, including all properties that affect rendering.
        /// </summary>
        public override int GetHashCode()
        {
            // Use HashCode.Combine to ensure all rendering-relevant properties are included
            var hash1 = HashCode.Combine(
                IconSize,
                Style,
                ColorScheme?.GetHashCode() ?? 0,
                TargetFrameRate,
                Quality,
                AntiAliasing,
                EnableEffects
            );
            
            var hash2 = HashCode.Combine(
                Animation?.GetHashCode() ?? 0,
                AdaptiveQuality,
                ChangeThreshold.GetHashCode()
            );
            
            return HashCode.Combine(hash1, hash2);
        }

        /// <summary>
        /// Determines whether the specified RenderConfiguration is equal to this instance.
        /// </summary>
        public override bool Equals(object? obj)
        {
            if (obj is not RenderConfiguration other) return false;
            return IconSize == other.IconSize &&
                   Style == other.Style &&
                   (ColorScheme?.Equals(other.ColorScheme) ?? other.ColorScheme == null) &&
                   TargetFrameRate == other.TargetFrameRate &&
                   Quality == other.Quality &&
                   AntiAliasing == other.AntiAliasing &&
                   EnableEffects == other.EnableEffects &&
                   (Animation?.Equals(other.Animation) ?? other.Animation == null) &&
                   AdaptiveQuality == other.AdaptiveQuality &&
                   Math.Abs(ChangeThreshold - other.ChangeThreshold) < 0.001;
        }
    }

    /// <summary>
    /// Available icon sizes for rendering.
    /// </summary>
    public enum IconSize
    {
        /// <summary>
        /// 16x16 pixels - minimal detail, 2-4 frequency bands.
        /// </summary>
        Small = 16,

        /// <summary>
        /// 24x24 pixels - balanced detail, 6-8 frequency bands.
        /// </summary>
        Medium = 24,

        /// <summary>
        /// 32x32 pixels - standard detail, 12-16 frequency bands.
        /// </summary>
        Large = 32,

        /// <summary>
        /// 48x48 pixels - high detail, full frequency spectrum.
        /// </summary>
        ExtraLarge = 48
    }

    /// <summary>
    /// Equalizer visualization styles.
    /// </summary>
    public enum EqualizerStyle
    {
        /// <summary>
        /// Traditional vertical bars.
        /// </summary>
        Bars,

        /// <summary>
        /// Circular dots visualization.
        /// </summary>
        Dots,

        /// <summary>
        /// Horizontal dashes visualization.
        /// </summary>
        Dashes,

        /// <summary>
        /// Waveform representation.
        /// </summary>
        Waveform,

        /// <summary>
        /// Spectrum analyzer style.
        /// </summary>
        Spectrum,

        /// <summary>
        /// Minimalist line visualization.
        /// </summary>
        Lines
    }

    /// <summary>
    /// Rendering quality levels.
    /// </summary>
    public enum RenderQuality
    {
        /// <summary>
        /// Low quality - fastest rendering, minimal effects.
        /// </summary>
        Low,

        /// <summary>
        /// Medium quality - balanced performance and visual quality.
        /// </summary>
        Medium,

        /// <summary>
        /// High quality - best visual quality, slower rendering.
        /// </summary>
        High,

        /// <summary>
        /// Ultra quality - maximum visual fidelity, requires powerful hardware.
        /// </summary>
        Ultra
    }

    /// <summary>
    /// Color scheme for equalizer visualization.
    /// </summary>
    public class ColorScheme
    {
        /// <summary>
        /// Primary color for active frequency bands.
        /// </summary>
        public Color PrimaryColor { get; set; } = Color.FromArgb(0, 120, 215); // Windows 11 accent blue

        /// <summary>
        /// Secondary color for gradient effects.
        /// </summary>
        public Color SecondaryColor { get; set; } = Color.FromArgb(0, 90, 158);

        /// <summary>
        /// Background color for the icon.
        /// </summary>
        public Color BackgroundColor { get; set; } = Color.Transparent;

        /// <summary>
        /// Border color for elements.
        /// </summary>
        public Color BorderColor { get; set; } = Color.FromArgb(60, 60, 60);

        /// <summary>
        /// Whether to use gradient effects.
        /// </summary>
        public bool UseGradient { get; set; } = true;

        /// <summary>
        /// Gradient direction for effects.
        /// </summary>
        public GradientDirection GradientDirection { get; set; } = GradientDirection.Vertical;

        /// <summary>
        /// Opacity level for the entire visualization (0.0-1.0).
        /// </summary>
        public float Opacity { get; set; } = 1.0f;

        /// <summary>
        /// Gets the hash code for this ColorScheme, including all color properties.
        /// </summary>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                PrimaryColor.ToArgb(),
                SecondaryColor.ToArgb(),
                BackgroundColor.ToArgb(),
                BorderColor.ToArgb(),
                UseGradient,
                GradientDirection,
                Opacity.GetHashCode()
            );
        }

        /// <summary>
        /// Determines whether the specified ColorScheme is equal to this instance.
        /// </summary>
        public override bool Equals(object? obj)
        {
            if (obj is not ColorScheme other) return false;
            return PrimaryColor.ToArgb() == other.PrimaryColor.ToArgb() &&
                   SecondaryColor.ToArgb() == other.SecondaryColor.ToArgb() &&
                   BackgroundColor.ToArgb() == other.BackgroundColor.ToArgb() &&
                   BorderColor.ToArgb() == other.BorderColor.ToArgb() &&
                   UseGradient == other.UseGradient &&
                   GradientDirection == other.GradientDirection &&
                   Math.Abs(Opacity - other.Opacity) < 0.001f;
        }
    }

    /// <summary>
    /// Gradient directions for visual effects.
    /// </summary>
    public enum GradientDirection
    {
        Horizontal,
        Vertical,
        Diagonal,
        Radial
    }

    /// <summary>
    /// Animation configuration settings.
    /// </summary>
    public class AnimationConfiguration
    {
        /// <summary>
        /// Smoothing factor for value transitions (0.0-1.0).
        /// </summary>
        public double SmoothingFactor { get; set; } = 0.8;

        /// <summary>
        /// Attack time for level increases in milliseconds.
        /// </summary>
        public double AttackTime { get; set; } = 10.0;

        /// <summary>
        /// Decay time for level decreases in milliseconds.
        /// </summary>
        public double DecayTime { get; set; } = 100.0;

        /// <summary>
        /// Whether to enable spring physics for animation.
        /// </summary>
        public bool EnableSpringPhysics { get; set; } = true;

        /// <summary>
        /// Spring stiffness for physics simulation.
        /// </summary>
        public float SpringStiffness { get; set; } = 200.0f;

        /// <summary>
        /// Spring damping for physics simulation.
        /// </summary>
        public float SpringDamping { get; set; } = 20.0f;

        /// <summary>
        /// Whether to enable beat detection effects.
        /// </summary>
        public bool EnableBeatEffects { get; set; } = true;

        /// <summary>
        /// Gets the hash code for this AnimationConfiguration.
        /// </summary>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                SmoothingFactor.GetHashCode(),
                AttackTime.GetHashCode(),
                DecayTime.GetHashCode(),
                EnableSpringPhysics,
                SpringStiffness.GetHashCode(),
                SpringDamping.GetHashCode(),
                EnableBeatEffects
            );
        }

        /// <summary>
        /// Determines whether the specified AnimationConfiguration is equal to this instance.
        /// </summary>
        public override bool Equals(object? obj)
        {
            if (obj is not AnimationConfiguration other) return false;
            return Math.Abs(SmoothingFactor - other.SmoothingFactor) < 0.001 &&
                   Math.Abs(AttackTime - other.AttackTime) < 0.001 &&
                   Math.Abs(DecayTime - other.DecayTime) < 0.001 &&
                   EnableSpringPhysics == other.EnableSpringPhysics &&
                   Math.Abs(SpringStiffness - other.SpringStiffness) < 0.001f &&
                   Math.Abs(SpringDamping - other.SpringDamping) < 0.001f &&
                   EnableBeatEffects == other.EnableBeatEffects;
        }
    }

    /// <summary>
    /// Basic rendering performance metrics.
    /// </summary>
    public class RenderingMetrics
    {
        /// <summary>
        /// Average rendering time in milliseconds.
        /// </summary>
        public double AverageRenderTime { get; set; }

        /// <summary>
        /// Current frame rate in frames per second.
        /// </summary>
        public double FrameRate { get; set; }

        /// <summary>
        /// Number of frames rendered.
        /// </summary>
        public long FrameCount { get; set; }

        /// <summary>
        /// Number of frames skipped for optimization.
        /// </summary>
        public long SkippedFrames { get; set; }

        /// <summary>
        /// Memory usage for rendering operations in bytes.
        /// </summary>
        public long MemoryUsage { get; set; }

        /// <summary>
        /// CPU usage percentage for rendering thread.
        /// </summary>
        public double CpuUsage { get; set; }
    }

    /// <summary>
    /// Detailed rendering performance metrics for analysis.
    /// </summary>
    public class DetailedRenderingMetrics : RenderingMetrics
    {
        /// <summary>
        /// 95th percentile rendering time in milliseconds.
        /// </summary>
        public double P95RenderTime { get; set; }

        /// <summary>
        /// Maximum rendering time recorded in milliseconds.
        /// </summary>
        public double MaxRenderTime { get; set; }

        /// <summary>
        /// Number of GDI+ objects currently allocated.
        /// </summary>
        public int GdiObjectCount { get; set; }

        /// <summary>
        /// Cache hit rate percentage (0.0-1.0).
        /// </summary>
        public double CacheHitRate { get; set; }

        /// <summary>
        /// Number of times adaptive quality was triggered.
        /// </summary>
        public long AdaptiveQualityTriggers { get; set; }

        /// <summary>
        /// Breakdown of rendering time by operation.
        /// </summary>
        public Dictionary<string, double> RenderingBreakdown { get; set; } = new();
    }

    /// <summary>
    /// Visualization theme configuration.
    /// </summary>
    public class VisualizationTheme
    {
        /// <summary>
        /// Name of the theme.
        /// </summary>
        public string Name { get; set; } = "Default";

        /// <summary>
        /// Theme author information.
        /// </summary>
        public string Author { get; set; } = "System";

        /// <summary>
        /// Theme version.
        /// </summary>
        public Version Version { get; set; } = new(1, 0);

        /// <summary>
        /// Color scheme for dark mode.
        /// </summary>
        public ColorScheme DarkMode { get; set; } = new();

        /// <summary>
        /// Color scheme for light mode.
        /// </summary>
        public ColorScheme LightMode { get; set; } = new();

        /// <summary>
        /// Color scheme for high contrast mode.
        /// </summary>
        public ColorScheme HighContrast { get; set; } = new();

        /// <summary>
        /// Default equalizer style for this theme.
        /// </summary>
        public EqualizerStyle DefaultStyle { get; set; } = EqualizerStyle.Bars;

        /// <summary>
        /// Custom parameters for theme extensions.
        /// </summary>
        public Dictionary<string, object> CustomParameters { get; set; } = new();
    }
}