using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.SystemTray.Interfaces
{
    /// <summary>
    /// Interface for Windows 11 theme detection and management.
    /// Provides automatic theme switching and system integration.
    /// </summary>
    public interface IThemeManager : INotifyPropertyChanged, IDisposable
    {
        /// <summary>
        /// Event fired when the system theme changes.
        /// </summary>
        event EventHandler<ThemeChangedEventArgs> ThemeChanged;

        /// <summary>
        /// Gets the currently active Windows theme.
        /// </summary>
        WindowsTheme CurrentSystemTheme { get; }

        /// <summary>
        /// Gets the currently active visualization theme.
        /// </summary>
        VisualizationTheme CurrentVisualizationTheme { get; }

        /// <summary>
        /// Gets the active color scheme based on current system theme.
        /// </summary>
        ColorScheme ActiveColorScheme { get; }

        /// <summary>
        /// Gets a value indicating whether theme monitoring is active.
        /// </summary>
        bool IsMonitoring { get; }

        /// <summary>
        /// Detects the current Windows 11 system theme.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task containing the detected theme information.</returns>
        Task<WindowsTheme> DetectSystemThemeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts monitoring for system theme changes.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous start operation.</returns>
        Task StartThemeMonitoringAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops monitoring for system theme changes.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous stop operation.</returns>
        Task StopThemeMonitoringAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Applies a new visualization theme.
        /// </summary>
        /// <param name="theme">Theme to apply.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous apply operation.</returns>
        Task ApplyVisualizationThemeAsync(VisualizationTheme theme, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the appropriate color scheme for the current system theme.
        /// </summary>
        /// <returns>Color scheme matching the current system state.</returns>
        ColorScheme GetCurrentColorScheme();

        /// <summary>
        /// Forces a refresh of the current theme detection.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous refresh operation.</returns>
        Task RefreshThemeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets Windows 11 system accent color.
        /// </summary>
        /// <returns>Current system accent color.</returns>
        System.Drawing.Color GetSystemAccentColor();

        /// <summary>
        /// Gets Windows 11 system background color.
        /// </summary>
        /// <returns>Current system background color.</returns>
        System.Drawing.Color GetSystemBackgroundColor();

        /// <summary>
        /// Gets Windows 11 system foreground color.
        /// </summary>
        /// <returns>Current system foreground color.</returns>
        System.Drawing.Color GetSystemForegroundColor();
    }

    /// <summary>
    /// Event arguments for theme change notifications.
    /// </summary>
    public class ThemeChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The previous theme that was active.
        /// </summary>
        public WindowsTheme? PreviousTheme { get; }

        /// <summary>
        /// The new theme that is now active.
        /// </summary>
        public WindowsTheme NewTheme { get; }

        /// <summary>
        /// The reason for the theme change.
        /// </summary>
        public ThemeChangeReason Reason { get; }

        /// <summary>
        /// Whether the change affects the visualization theme.
        /// </summary>
        public bool AffectsVisualization { get; }

        public ThemeChangedEventArgs(WindowsTheme? previousTheme, WindowsTheme newTheme, 
            ThemeChangeReason reason, bool affectsVisualization = true)
        {
            PreviousTheme = previousTheme;
            NewTheme = newTheme;
            Reason = reason;
            AffectsVisualization = affectsVisualization;
        }
    }

    /// <summary>
    /// Reasons for theme changes.
    /// </summary>
    public enum ThemeChangeReason
    {
        /// <summary>
        /// User manually changed the system theme.
        /// </summary>
        UserChanged,

        /// <summary>
        /// System automatically switched theme (e.g., time-based).
        /// </summary>
        SystemAutomatic,

        /// <summary>
        /// Application-initiated theme refresh.
        /// </summary>
        ApplicationRefresh,

        /// <summary>
        /// Initial theme detection on startup.
        /// </summary>
        InitialDetection,

        /// <summary>
        /// Theme change due to accessibility settings.
        /// </summary>
        AccessibilityChange
    }

    /// <summary>
    /// Windows 11 system theme information.
    /// </summary>
    public class WindowsTheme
    {
        /// <summary>
        /// Whether the system is using light mode.
        /// </summary>
        public bool IsLightMode { get; set; }

        /// <summary>
        /// Whether applications should use light theme.
        /// </summary>
        public bool AppsUseLightTheme { get; set; }

        /// <summary>
        /// Whether the system uses light theme for system chrome.
        /// </summary>
        public bool SystemUsesLightTheme { get; set; }

        /// <summary>
        /// Whether high contrast mode is enabled.
        /// </summary>
        public bool IsHighContrast { get; set; }

        /// <summary>
        /// System accent color.
        /// </summary>
        public System.Drawing.Color AccentColor { get; set; }

        /// <summary>
        /// System background color.
        /// </summary>
        public System.Drawing.Color BackgroundColor { get; set; }

        /// <summary>
        /// System foreground (text) color.
        /// </summary>
        public System.Drawing.Color ForegroundColor { get; set; }

        /// <summary>
        /// Whether transparency effects are enabled.
        /// </summary>
        public bool TransparencyEnabled { get; set; }

        /// <summary>
        /// Whether animations are enabled.
        /// </summary>
        public bool AnimationsEnabled { get; set; }

        /// <summary>
        /// DWM colorization information.
        /// </summary>
        public DwmColorization DwmColorization { get; set; } = new();

        /// <summary>
        /// Timestamp when this theme information was detected.
        /// </summary>
        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets the effective theme mode for UI purposes.
        /// </summary>
        public ThemeMode EffectiveMode
        {
            get
            {
                if (IsHighContrast) return ThemeMode.HighContrast;
                return AppsUseLightTheme ? ThemeMode.Light : ThemeMode.Dark;
            }
        }
    }

    /// <summary>
    /// DWM (Desktop Window Manager) colorization information.
    /// </summary>
    public class DwmColorization
    {
        /// <summary>
        /// DWM colorization color.
        /// </summary>
        public System.Drawing.Color ColorizationColor { get; set; }

        /// <summary>
        /// Whether colorization affects window borders.
        /// </summary>
        public bool ColorizationAfterglow { get; set; }

        /// <summary>
        /// Colorization blur balance.
        /// </summary>
        public uint ColorizationBlurBalance { get; set; }

        /// <summary>
        /// Colorization color balance.
        /// </summary>
        public uint ColorizationColorBalance { get; set; }

        /// <summary>
        /// Whether composition (Aero) is enabled.
        /// </summary>
        public bool CompositionEnabled { get; set; }
    }

    /// <summary>
    /// Theme modes for UI adaptation.
    /// </summary>
    public enum ThemeMode
    {
        /// <summary>
        /// Light theme mode.
        /// </summary>
        Light,

        /// <summary>
        /// Dark theme mode.
        /// </summary>
        Dark,

        /// <summary>
        /// High contrast mode for accessibility.
        /// </summary>
        HighContrast
    }
}