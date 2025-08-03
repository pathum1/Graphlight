using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.SystemTray.Theming
{
    /// <summary>
    /// Windows 11 theme detection and management implementation.
    /// Provides automatic theme switching and system integration.
    /// </summary>
    public sealed class ThemeManager : IThemeManager
    {
        private readonly ILogger<ThemeManager> _logger;
        private readonly object _themeLock = new();
        
        private WindowsTheme _currentSystemTheme;
        private VisualizationTheme _currentVisualizationTheme;
        private ColorScheme _activeColorScheme;
        
        private bool _isMonitoring;
        private bool _disposed;
        
        private CancellationTokenSource? _monitoringCancellation;
        private Task? _monitoringTask;

        // Windows Registry paths for theme detection
        private const string PersonalizeKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string DwmKeyPath = @"SOFTWARE\Microsoft\Windows\DWM";
        private const string AccessibilityKeyPath = @"Control Panel\Accessibility\HighContrast";

        /// <summary>
        /// Initializes a new instance of the ThemeManager.
        /// </summary>
        /// <param name="logger">Logger for diagnostic information.</param>
        public ThemeManager(ILogger<ThemeManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Initialize with default values
            _currentSystemTheme = new WindowsTheme();
            _currentVisualizationTheme = CreateDefaultVisualizationTheme();
            _activeColorScheme = _currentVisualizationTheme.DarkMode;
            
            _logger.LogDebug("ThemeManager initialized");
        }

        #region Events

        /// <inheritdoc />
        public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion

        #region Properties

        /// <inheritdoc />
        public WindowsTheme CurrentSystemTheme 
        { 
            get { lock (_themeLock) { return _currentSystemTheme; } }
        }

        /// <inheritdoc />
        public VisualizationTheme CurrentVisualizationTheme 
        { 
            get { lock (_themeLock) { return _currentVisualizationTheme; } }
        }

        /// <inheritdoc />
        public ColorScheme ActiveColorScheme 
        { 
            get { lock (_themeLock) { return _activeColorScheme; } }
        }

        /// <inheritdoc />
        public bool IsMonitoring => _isMonitoring;

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public async Task<WindowsTheme> DetectSystemThemeAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ThemeManager));

            _logger.LogDebug("Detecting Windows 11 system theme");

            try
            {
                var theme = new WindowsTheme();

                // Detect personalization settings
                await DetectPersonalizationSettings(theme, cancellationToken);
                
                // Detect accessibility settings
                await DetectAccessibilitySettings(theme, cancellationToken);
                
                // Detect DWM colorization
                await DetectDwmColorization(theme, cancellationToken);
                
                // Get system colors
                await DetectSystemColors(theme, cancellationToken);

                theme.DetectedAt = DateTime.UtcNow;

                _logger.LogInformation("Theme detected: {EffectiveMode}, Light={IsLight}, HighContrast={IsHighContrast}", 
                    theme.EffectiveMode, theme.IsLightMode, theme.IsHighContrast);

                lock (_themeLock)
                {
                    _currentSystemTheme = theme;
                }

                return theme;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to detect system theme");
                throw;
            }
        }

        /// <inheritdoc />
        public Task StartThemeMonitoringAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ThemeManager));

            if (_isMonitoring)
            {
                _logger.LogWarning("Theme monitoring is already active");
                return Task.CompletedTask;
            }

            _logger.LogInformation("Starting Windows 11 theme monitoring");

            try
            {
                _monitoringCancellation = new CancellationTokenSource();
                var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _monitoringCancellation.Token).Token;

                _monitoringTask = MonitorThemeChangesAsync(combinedToken);
                _isMonitoring = true;

                _logger.LogDebug("Theme monitoring started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start theme monitoring");
                throw;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task StopThemeMonitoringAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ThemeManager));

            if (!_isMonitoring)
                return;

            _logger.LogInformation("Stopping theme monitoring");

            try
            {
                _monitoringCancellation?.Cancel();
                
                if (_monitoringTask != null)
                {
                    await _monitoringTask;
                }

                _isMonitoring = false;
                _logger.LogDebug("Theme monitoring stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping theme monitoring");
                throw;
            }
        }

        /// <inheritdoc />
        public Task ApplyVisualizationThemeAsync(VisualizationTheme theme, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ThemeManager));

            if (theme == null)
                throw new ArgumentNullException(nameof(theme));

            _logger.LogInformation("Applying visualization theme: {ThemeName} v{Version}", theme.Name, theme.Version);

            try
            {
                lock (_themeLock)
                {
                    _currentVisualizationTheme = theme;
                    
                    // Update active color scheme based on current system theme
                    _activeColorScheme = _currentSystemTheme.EffectiveMode switch
                    {
                        ThemeMode.Light => theme.LightMode,
                        ThemeMode.Dark => theme.DarkMode,
                        ThemeMode.HighContrast => theme.HighContrast,
                        _ => theme.DarkMode
                    };
                }

                OnPropertyChanged(nameof(CurrentVisualizationTheme));
                OnPropertyChanged(nameof(ActiveColorScheme));

                _logger.LogDebug("Visualization theme applied successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply visualization theme");
                throw;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public ColorScheme GetCurrentColorScheme()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ThemeManager));

            lock (_themeLock)
            {
                return _activeColorScheme;
            }
        }

        /// <inheritdoc />
        public async Task RefreshThemeAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ThemeManager));

            _logger.LogDebug("Refreshing theme detection");

            try
            {
                var previousTheme = _currentSystemTheme;
                var newTheme = await DetectSystemThemeAsync(cancellationToken);

                // Check if theme actually changed
                if (HasThemeChanged(previousTheme, newTheme))
                {
                    await HandleThemeChange(previousTheme, newTheme, ThemeChangeReason.ApplicationRefresh);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh theme");
                throw;
            }
        }

        /// <inheritdoc />
        public Color GetSystemAccentColor()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ThemeManager));

            try
            {
                // Try to get from current theme first
                lock (_themeLock)
                {
                    if (_currentSystemTheme.AccentColor != Color.Empty)
                        return _currentSystemTheme.AccentColor;
                }

                // Fallback to Windows API
                var accentColor = GetWindowsAccentColor();
                return accentColor != Color.Empty ? accentColor : Color.FromArgb(0, 120, 215);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get system accent color, using default");
                return Color.FromArgb(0, 120, 215); // Default Windows 11 blue
            }
        }

        /// <inheritdoc />
        public Color GetSystemBackgroundColor()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ThemeManager));

            try
            {
                lock (_themeLock)
                {
                    if (_currentSystemTheme.BackgroundColor != Color.Empty)
                        return _currentSystemTheme.BackgroundColor;
                }

                // Fallback based on theme mode
                return _currentSystemTheme.EffectiveMode switch
                {
                    ThemeMode.Light => Color.White,
                    ThemeMode.Dark => Color.FromArgb(32, 32, 32),
                    ThemeMode.HighContrast => SystemColors.Window,
                    _ => Color.FromArgb(32, 32, 32)
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get system background color, using default");
                return Color.FromArgb(32, 32, 32);
            }
        }

        /// <inheritdoc />
        public Color GetSystemForegroundColor()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ThemeManager));

            try
            {
                lock (_themeLock)
                {
                    if (_currentSystemTheme.ForegroundColor != Color.Empty)
                        return _currentSystemTheme.ForegroundColor;
                }

                // Fallback based on theme mode
                return _currentSystemTheme.EffectiveMode switch
                {
                    ThemeMode.Light => Color.Black,
                    ThemeMode.Dark => Color.White,
                    ThemeMode.HighContrast => SystemColors.WindowText,
                    _ => Color.White
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get system foreground color, using default");
                return Color.White;
            }
        }

        #endregion

        #region Private Methods

        private async Task DetectPersonalizationSettings(WindowsTheme theme, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
                    if (key != null)
                    {
                        // Apps use light theme (0 = dark, 1 = light)
                        var appsUseLightTheme = key.GetValue("AppsUseLightTheme", 0);
                        theme.AppsUseLightTheme = Convert.ToInt32(appsUseLightTheme) == 1;

                        // System uses light theme (0 = dark, 1 = light)
                        var systemUsesLightTheme = key.GetValue("SystemUsesLightTheme", 0);
                        theme.SystemUsesLightTheme = Convert.ToInt32(systemUsesLightTheme) == 1;

                        // Overall light mode is typically based on apps setting
                        theme.IsLightMode = theme.AppsUseLightTheme;

                        // Check for transparency
                        var enableTransparency = key.GetValue("EnableTransparency", 1);
                        theme.TransparencyEnabled = Convert.ToInt32(enableTransparency) == 1;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read personalization settings");
                }
            }, cancellationToken);
        }

        private async Task DetectAccessibilitySettings(WindowsTheme theme, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(AccessibilityKeyPath);
                    if (key != null)
                    {
                        var highContrast = key.GetValue("Flags", "0");
                        theme.IsHighContrast = Convert.ToInt32(highContrast) == 1;
                    }

                    // Also check system metrics
                    theme.IsHighContrast |= SystemInformation.HighContrast;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read accessibility settings");
                }
            }, cancellationToken);
        }

        private async Task DetectDwmColorization(WindowsTheme theme, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Try to get DWM colorization info using Windows API
                    if (DwmGetColorizationColor(out uint colorizationColor, out bool opaque) == 0)
                    {
                        theme.DwmColorization.ColorizationColor = Color.FromArgb((int)colorizationColor);
                        theme.DwmColorization.CompositionEnabled = DwmIsCompositionEnabled();
                    }

                    // Check registry for additional DWM settings
                    using var key = Registry.CurrentUser.OpenSubKey(DwmKeyPath);
                    if (key != null)
                    {
                        var colorization = key.GetValue("ColorPrevalence", 0);
                        theme.DwmColorization.ColorizationAfterglow = Convert.ToInt32(colorization) == 1;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read DWM colorization settings");
                }
            }, cancellationToken);
        }

        private async Task DetectSystemColors(WindowsTheme theme, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Get system accent color
                    theme.AccentColor = GetWindowsAccentColor();

                    // Get background and foreground colors based on theme
                    if (theme.IsHighContrast)
                    {
                        theme.BackgroundColor = SystemColors.Window;
                        theme.ForegroundColor = SystemColors.WindowText;
                    }
                    else if (theme.IsLightMode)
                    {
                        theme.BackgroundColor = Color.White;
                        theme.ForegroundColor = Color.Black;
                    }
                    else
                    {
                        theme.BackgroundColor = Color.FromArgb(32, 32, 32);
                        theme.ForegroundColor = Color.White;
                    }

                    // Check animation settings
                    theme.AnimationsEnabled = !SystemInformation.IsMinimizeRestoreAnimationEnabled;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to detect system colors");
                }
            }, cancellationToken);
        }

        private Color GetWindowsAccentColor()
        {
            try
            {
                // Try to get accent color from Windows 10/11 API
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\DWM");
                if (key != null)
                {
                    var accentColor = key.GetValue("AccentColor");
                    if (accentColor != null)
                    {
                        var colorValue = Convert.ToUInt32(accentColor);
                        return Color.FromArgb((int)colorValue);
                    }
                }

                // Fallback to DWM colorization
                if (DwmGetColorizationColor(out uint colorizationColor, out _) == 0)
                {
                    return Color.FromArgb((int)colorizationColor);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get Windows accent color");
            }

            return Color.FromArgb(0, 120, 215); // Default Windows 11 blue
        }

        private async Task MonitorThemeChangesAsync(CancellationToken cancellationToken)
        {
            const int pollInterval = 2000; // Check every 2 seconds

            try
            {
                var lastTheme = _currentSystemTheme;

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(pollInterval, cancellationToken);

                        var currentTheme = await DetectSystemThemeAsync(cancellationToken);

                        if (HasThemeChanged(lastTheme, currentTheme))
                        {
                            await HandleThemeChange(lastTheme, currentTheme, ThemeChangeReason.SystemAutomatic);
                            lastTheme = currentTheme;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error during theme monitoring");
                        // Continue monitoring despite errors
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Theme monitoring cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Theme monitoring failed");
            }
        }

        private bool HasThemeChanged(WindowsTheme previous, WindowsTheme current)
        {
            return previous.IsLightMode != current.IsLightMode ||
                   previous.IsHighContrast != current.IsHighContrast ||
                   previous.TransparencyEnabled != current.TransparencyEnabled ||
                   !previous.AccentColor.Equals(current.AccentColor);
        }

        private async Task HandleThemeChange(WindowsTheme? previousTheme, WindowsTheme newTheme, ThemeChangeReason reason)
        {
            _logger.LogInformation("Theme changed: {PreviousMode} → {NewMode}, Reason: {Reason}",
                previousTheme?.EffectiveMode, newTheme.EffectiveMode, reason);

            lock (_themeLock)
            {
                _currentSystemTheme = newTheme;
                
                // Update active color scheme
                _activeColorScheme = newTheme.EffectiveMode switch
                {
                    ThemeMode.Light => _currentVisualizationTheme.LightMode,
                    ThemeMode.Dark => _currentVisualizationTheme.DarkMode,
                    ThemeMode.HighContrast => _currentVisualizationTheme.HighContrast,
                    _ => _currentVisualizationTheme.DarkMode
                };
            }

            // Notify property changes
            OnPropertyChanged(nameof(CurrentSystemTheme));
            OnPropertyChanged(nameof(ActiveColorScheme));

            // Fire theme changed event
            var args = new ThemeChangedEventArgs(previousTheme, newTheme, reason, true);
            ThemeChanged?.Invoke(this, args);

            await Task.CompletedTask;
        }

        private VisualizationTheme CreateDefaultVisualizationTheme()
        {
            return new VisualizationTheme
            {
                Name = "Windows 11 Default",
                Author = "System",
                Version = new Version(1, 0),
                DarkMode = new ColorScheme
                {
                    PrimaryColor = Color.FromArgb(0, 120, 215),
                    SecondaryColor = Color.FromArgb(0, 90, 158),
                    BackgroundColor = Color.Transparent,
                    BorderColor = Color.FromArgb(60, 60, 60),
                    UseGradient = true,
                    GradientDirection = GradientDirection.Vertical,
                    Opacity = 1.0f
                },
                LightMode = new ColorScheme
                {
                    PrimaryColor = Color.FromArgb(0, 120, 215),
                    SecondaryColor = Color.FromArgb(0, 90, 158),
                    BackgroundColor = Color.Transparent,
                    BorderColor = Color.FromArgb(200, 200, 200),
                    UseGradient = true,
                    GradientDirection = GradientDirection.Vertical,
                    Opacity = 1.0f
                },
                HighContrast = new ColorScheme
                {
                    PrimaryColor = SystemColors.Highlight,
                    SecondaryColor = SystemColors.HighlightText,
                    BackgroundColor = Color.Transparent,
                    BorderColor = SystemColors.WindowText,
                    UseGradient = false,
                    GradientDirection = GradientDirection.Vertical,
                    Opacity = 1.0f
                },
                DefaultStyle = EqualizerStyle.Bars
            };
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Windows API

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetColorizationColor(out uint ColorizationColor, out bool ColorizationOpaqueBlend);

        [DllImport("dwmapi.dll")]
        private static extern bool DwmIsCompositionEnabled();

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the theme manager and stops monitoring.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                
                try
                {
                    _ = StopThemeMonitoringAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping theme monitoring during disposal");
                }

                _monitoringCancellation?.Dispose();
                
                _logger.LogDebug("ThemeManager disposed");
            }
        }

        #endregion
    }
}