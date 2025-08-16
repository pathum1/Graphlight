using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Core.Interfaces;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.SystemTray
{
    /// <summary>
    /// High-performance taskbar overlay manager for real-time audio visualization.
    /// Creates a transparent overlay window positioned on the Windows taskbar.
    /// </summary>
    public sealed class TaskbarOverlayManager : ITaskbarOverlayManager
    {
        private readonly ILogger<TaskbarOverlayManager> _logger;
        private readonly IIconRenderer _iconRenderer;
        
        private OverlayWindow? _overlayWindow;
        private OverlayConfiguration _configuration;
        private OverlayStatus _status = OverlayStatus.NotInitialized;
        private System.Threading.Timer? _updateTimer;
        private SpectrumDataEventArgs? _latestSpectrumData;
        
        private readonly object _lock = new();
        private volatile bool _disposed;

        public event EventHandler<OverlayStatusChangedEventArgs>? StatusChanged;

        public bool IsActive => _status == OverlayStatus.Active;
        public OverlayConfiguration Configuration => _configuration;

        public TaskbarOverlayManager(ILogger<TaskbarOverlayManager> logger, IIconRenderer iconRenderer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _iconRenderer = iconRenderer ?? throw new ArgumentNullException(nameof(iconRenderer));
            _configuration = new OverlayConfiguration();
        }

        public Task InitializeAsync(OverlayConfiguration configuration, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TaskbarOverlayManager));

            _logger.LogInformation("Initializing taskbar overlay manager");

            try
            {
                _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

                lock (_lock)
                {
                    if (_status != OverlayStatus.NotInitialized)
                    {
                        _logger.LogWarning("TaskbarOverlayManager is already initialized");
                        return Task.CompletedTask;
                    }

                    ChangeStatus(OverlayStatus.Hidden, "Initialization completed");
                }

                _logger.LogInformation("TaskbarOverlayManager initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize TaskbarOverlayManager");
                ChangeStatus(OverlayStatus.Error, $"Initialization failed: {ex.Message}");
                throw;
            }

            return Task.CompletedTask;
        }

        public async Task ShowAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TaskbarOverlayManager));

            if (_status == OverlayStatus.NotInitialized)
                throw new InvalidOperationException("TaskbarOverlayManager must be initialized before showing");

            _logger.LogInformation("Showing taskbar overlay");

            try
            {
                await CreateOverlayWindowAsync();
                StartUpdateTimer();
                ChangeStatus(OverlayStatus.Active, "Overlay shown successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show taskbar overlay");
                ChangeStatus(OverlayStatus.Error, $"Show failed: {ex.Message}");
                throw;
            }
        }

        public async Task HideAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TaskbarOverlayManager));

            _logger.LogInformation("Hiding taskbar overlay");

            try
            {
                StopUpdateTimer();
                await DestroyOverlayWindowAsync();
                ChangeStatus(OverlayStatus.Hidden, "Overlay hidden successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to hide taskbar overlay");
                ChangeStatus(OverlayStatus.Error, $"Hide failed: {ex.Message}");
                throw;
            }
        }

        public void UpdateVisualization(SpectrumDataEventArgs spectrumData)
        {
            if (_disposed || _status != OverlayStatus.Active)
                return;

            _latestSpectrumData = spectrumData;
        }

        public async Task UpdateConfigurationAsync(OverlayConfiguration configuration, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TaskbarOverlayManager));

            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            if (_status == OverlayStatus.Active)
            {
                // Update the existing overlay window
                await UpdateOverlayWindowAsync();
            }
        }

        public async Task RefreshTaskbarPositionAsync()
        {
            if (_disposed || _status != OverlayStatus.Active)
                return;

            try
            {
                await UpdateOverlayWindowAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh taskbar position");
            }
        }

        public async Task UpdateSettingsAsync(object settings, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TaskbarOverlayManager));

            if (settings == null)
            {
                _logger.LogWarning("UpdateSettingsAsync called with null settings");
                return;
            }

            try
            {
                _logger.LogInformation("Updating TaskbarOverlayManager settings");

                // Use reflection to access ApplicationSettings properties
                var settingsType = settings.GetType();

                // Create new overlay configuration instance instead of mutating existing one
                var overlayConfig = new OverlayConfiguration
                {
                    Enabled = _configuration.Enabled,
                    Position = _configuration.Position,
                    Width = _configuration.Width,
                    Height = _configuration.Height,
                    Margin = _configuration.Margin,
                    AutoHide = _configuration.AutoHide,
                    AutoHideDelay = _configuration.AutoHideDelay,
                    RenderConfiguration = new RenderConfiguration
                    {
                        // Copy existing values first to preserve settings not being updated
                        IconSize = _configuration.RenderConfiguration?.IconSize ?? IconSize.Medium,
                        Style = _configuration.RenderConfiguration?.Style ?? EqualizerStyle.Bars,
                        TargetFrameRate = _configuration.RenderConfiguration?.TargetFrameRate ?? 60,
                        Quality = _configuration.RenderConfiguration?.Quality ?? RenderQuality.High,
                        AntiAliasing = _configuration.RenderConfiguration?.AntiAliasing ?? true,
                        EnableEffects = _configuration.RenderConfiguration?.EnableEffects ?? true,
                        AdaptiveQuality = _configuration.RenderConfiguration?.AdaptiveQuality ?? true,
                        ChangeThreshold = _configuration.RenderConfiguration?.ChangeThreshold ?? 0.02,
                        ColorScheme = new ColorScheme
                        {
                            // Copy existing color scheme values to preserve settings not being updated
                            PrimaryColor = _configuration.RenderConfiguration?.ColorScheme?.PrimaryColor ?? Color.FromArgb(0, 120, 215),
                            SecondaryColor = _configuration.RenderConfiguration?.ColorScheme?.SecondaryColor ?? Color.FromArgb(0, 90, 158),
                            BackgroundColor = _configuration.RenderConfiguration?.ColorScheme?.BackgroundColor ?? Color.Transparent,
                            BorderColor = _configuration.RenderConfiguration?.ColorScheme?.BorderColor ?? Color.FromArgb(60, 60, 60),
                            UseGradient = _configuration.RenderConfiguration?.ColorScheme?.UseGradient ?? true,
                            GradientDirection = _configuration.RenderConfiguration?.ColorScheme?.GradientDirection ?? GradientDirection.Vertical,
                            Opacity = _configuration.RenderConfiguration?.ColorScheme?.Opacity ?? 1.0f
                        },
                        Animation = new AnimationConfiguration
                        {
                            // Copy existing animation values to preserve settings not being updated
                            SmoothingFactor = _configuration.RenderConfiguration?.Animation?.SmoothingFactor ?? 0.8,
                            AttackTime = _configuration.RenderConfiguration?.Animation?.AttackTime ?? 10.0,
                            DecayTime = _configuration.RenderConfiguration?.Animation?.DecayTime ?? 100.0,
                            EnableSpringPhysics = _configuration.RenderConfiguration?.Animation?.EnableSpringPhysics ?? true,
                            SpringStiffness = _configuration.RenderConfiguration?.Animation?.SpringStiffness ?? 200.0f,
                            SpringDamping = _configuration.RenderConfiguration?.Animation?.SpringDamping ?? 20.0f,
                            EnableBeatEffects = _configuration.RenderConfiguration?.Animation?.EnableBeatEffects ?? true
                        }
                    }
                };

                // Update UpdateFrequency from UpdateInterval setting
                var updateIntervalProp = settingsType.GetProperty("UpdateInterval");
                if (updateIntervalProp?.GetValue(settings) is double updateInterval && updateInterval > 0)
                {
                    overlayConfig.UpdateFrequency = Math.Max(15, (int)(1000.0 / updateInterval));
                    _logger.LogDebug("Updated overlay frequency to {Frequency}Hz from {Interval}ms interval", 
                        overlayConfig.UpdateFrequency, updateInterval);
                }

                // Update Opacity
                var opacityProp = settingsType.GetProperty("Opacity");
                if (opacityProp?.GetValue(settings) is float opacity)
                {
                    overlayConfig.Opacity = opacity;
                    _logger.LogDebug("Updated overlay opacity to {Opacity}", opacity);
                }

                // Update RenderConfiguration with visualization settings
                var renderConfig = overlayConfig.RenderConfiguration;

                // IconSize
                var iconSizeProp = settingsType.GetProperty("IconSize");
                if (iconSizeProp?.GetValue(settings) is object iconSize)
                {
                    if (Enum.TryParse<IconSize>(iconSize.ToString(), out var parsedSize))
                    {
                        renderConfig.IconSize = parsedSize;
                        _logger.LogDebug("Updated icon size to {Size}", parsedSize);
                    }
                }

                // VisualizationStyle
                var styleProp = settingsType.GetProperty("VisualizationStyle");
                if (styleProp?.GetValue(settings) is object style)
                {
                    if (Enum.TryParse<EqualizerStyle>(style.ToString(), out var parsedStyle))
                    {
                        renderConfig.Style = parsedStyle;
                        _logger.LogDebug("Updated visualization style to {Style}", parsedStyle);
                    }
                }

                // RenderQuality
                var qualityProp = settingsType.GetProperty("RenderQuality");
                if (qualityProp?.GetValue(settings) is object quality)
                {
                    if (Enum.TryParse<RenderQuality>(quality.ToString(), out var parsedQuality))
                    {
                        renderConfig.Quality = parsedQuality;
                        _logger.LogDebug("Updated render quality to {Quality}", parsedQuality);
                    }
                }

                // MaxFrameRate
                var maxFrameRateProp = settingsType.GetProperty("MaxFrameRate");
                if (maxFrameRateProp?.GetValue(settings) is int maxFrameRate && maxFrameRate > 0)
                {
                    renderConfig.TargetFrameRate = maxFrameRate;
                    _logger.LogDebug("Updated target frame rate to {FrameRate}", maxFrameRate);
                }

                // EnableAnimations
                var enableAnimationsProp = settingsType.GetProperty("EnableAnimations");
                if (enableAnimationsProp?.GetValue(settings) is bool enableAnimations)
                {
                    _logger.LogDebug("Animations enabled: {Enabled}", enableAnimations);
                }

                // EnableEffects
                var enableEffectsProp = settingsType.GetProperty("EnableEffects");
                if (enableEffectsProp?.GetValue(settings) is bool enableEffects)
                {
                    renderConfig.EnableEffects = enableEffects;
                    _logger.LogDebug("Updated effects enabled to {Enabled}", enableEffects);
                }

                // AdaptiveQuality
                var adaptiveQualityProp = settingsType.GetProperty("AdaptiveQuality");
                if (adaptiveQualityProp?.GetValue(settings) is bool adaptiveQuality)
                {
                    renderConfig.AdaptiveQuality = adaptiveQuality;
                    _logger.LogDebug("Updated adaptive quality to {Enabled}", adaptiveQuality);
                }

                // ChangeThreshold
                var changeThresholdProp = settingsType.GetProperty("ChangeThreshold");
                if (changeThresholdProp?.GetValue(settings) is double changeThreshold)
                {
                    renderConfig.ChangeThreshold = changeThreshold;
                    _logger.LogDebug("Updated change threshold to {Threshold}", changeThreshold);
                }

                // Update color scheme - ensure we handle custom colors properly
                var colorScheme = renderConfig.ColorScheme;

                // Check UseCustomColors property first
                var useCustomColorsProp = settingsType.GetProperty("UseCustomColors");
                var useCustomColors = useCustomColorsProp?.GetValue(settings) is bool customColors && customColors;

                // Enhanced logging to track the UseCustomColors flag issue
                _logger.LogInformation("COLOR DEBUGGING: UseCustomColors property found: {PropertyFound}, Raw value: {RawValue}, Parsed: {UseCustomColors}", 
                    useCustomColorsProp != null, useCustomColorsProp?.GetValue(settings), useCustomColors);

                // Enhanced logging to show available color properties
                var primaryColorProp = settingsType.GetProperty("CustomPrimaryColor");
                var secondaryColorProp = settingsType.GetProperty("CustomSecondaryColor");
                var primaryColorValue = primaryColorProp?.GetValue(settings);
                var secondaryColorValue = secondaryColorProp?.GetValue(settings);
                
                _logger.LogInformation("COLOR DEBUGGING: Available color properties - Primary: {PrimaryProp} (value: {PrimaryValue}), Secondary: {SecondaryProp} (value: {SecondaryValue})", 
                    primaryColorProp != null, primaryColorValue, secondaryColorProp != null, secondaryColorValue);

                if (useCustomColors)
                {
                    // Apply custom colors when UseCustomColors is true
                    if (primaryColorValue is System.Drawing.Color primaryColor)
                    {
                        colorScheme.PrimaryColor = primaryColor;
                        _logger.LogInformation("COLOR DEBUGGING: Applied custom primary color: {Color} (A={A}, R={R}, G={G}, B={B})", 
                            primaryColor.Name, primaryColor.A, primaryColor.R, primaryColor.G, primaryColor.B);
                    }

                    if (secondaryColorValue is System.Drawing.Color secondaryColor)
                    {
                        colorScheme.SecondaryColor = secondaryColor;
                        _logger.LogInformation("COLOR DEBUGGING: Applied custom secondary color: {Color} (A={A}, R={R}, G={G}, B={B})", 
                            secondaryColor.Name, secondaryColor.A, secondaryColor.R, secondaryColor.G, secondaryColor.B);
                    }
                }
                else
                {
                    // Use default theme colors when UseCustomColors is false
                    colorScheme.PrimaryColor = Color.FromArgb(0, 120, 215); // Windows 11 accent blue
                    colorScheme.SecondaryColor = Color.FromArgb(0, 90, 158);
                    _logger.LogInformation("COLOR DEBUGGING: Using default theme colors - UseCustomColors is false, Applied Primary: {Primary}, Secondary: {Secondary}", 
                        colorScheme.PrimaryColor, colorScheme.SecondaryColor);
                }

                // EnableGradient
                var enableGradientProp = settingsType.GetProperty("EnableGradient");
                if (enableGradientProp?.GetValue(settings) is bool enableGradient)
                {
                    colorScheme.UseGradient = enableGradient;
                    _logger.LogDebug("Updated gradient enabled to {Enabled}", enableGradient);
                }

                // GradientDirection
                var gradientDirectionProp = settingsType.GetProperty("GradientDirection");
                if (gradientDirectionProp?.GetValue(settings) is object gradientDirection)
                {
                    if (Enum.TryParse<GradientDirection>(gradientDirection.ToString(), out var parsedDirection))
                    {
                        colorScheme.GradientDirection = parsedDirection;
                        _logger.LogDebug("Updated gradient direction to {Direction}", parsedDirection);
                    }
                }

                // Update animation configuration
                if (renderConfig.Animation == null)
                    renderConfig.Animation = new AnimationConfiguration();

                var animConfig = renderConfig.Animation;

                // SmoothingFactor
                var smoothingFactorProp = settingsType.GetProperty("SmoothingFactor");
                if (smoothingFactorProp?.GetValue(settings) is double smoothingFactor)
                {
                    animConfig.SmoothingFactor = smoothingFactor;
                    _logger.LogDebug("Updated smoothing factor to {Factor}", smoothingFactor);
                }

                // AnimationSpeed
                var animSpeedProp = settingsType.GetProperty("AnimationSpeed");
                if (animSpeedProp?.GetValue(settings) is double animSpeed)
                {
                    // Convert animation speed to attack/decay times
                    var baseAttackTime = 10.0;
                    var baseDecayTime = 100.0;
                    animConfig.AttackTime = baseAttackTime / animSpeed;
                    animConfig.DecayTime = baseDecayTime / animSpeed;
                    _logger.LogDebug("Updated animation speed to {Speed} (attack={Attack}ms, decay={Decay}ms)", 
                        animSpeed, animConfig.AttackTime, animConfig.DecayTime);
                }

                // EnableBeatDetection
                var beatDetectionProp = settingsType.GetProperty("EnableBeatDetection");
                if (beatDetectionProp?.GetValue(settings) is bool enableBeatDetection)
                {
                    animConfig.EnableBeatEffects = enableBeatDetection;
                    _logger.LogDebug("Updated beat detection to {Enabled}", enableBeatDetection);
                }

                // EnableSpringPhysics
                var springPhysicsProp = settingsType.GetProperty("EnableSpringPhysics");
                if (springPhysicsProp?.GetValue(settings) is bool enableSpringPhysics)
                {
                    animConfig.EnableSpringPhysics = enableSpringPhysics;
                    _logger.LogDebug("Updated spring physics to {Enabled}", enableSpringPhysics);
                }

                // SpringStiffness
                var springStiffnessProp = settingsType.GetProperty("SpringStiffness");
                if (springStiffnessProp?.GetValue(settings) is float springStiffness)
                {
                    animConfig.SpringStiffness = springStiffness;
                    _logger.LogDebug("Updated spring stiffness to {Stiffness}", springStiffness);
                }

                // SpringDamping
                var springDampingProp = settingsType.GetProperty("SpringDamping");
                if (springDampingProp?.GetValue(settings) is float springDamping)
                {
                    animConfig.SpringDamping = springDamping;
                    _logger.LogDebug("Updated spring damping to {Damping}", springDamping);
                }

                // FrequencyBands - store for later use in overlay window configuration
                int? frequencyBands = null;
                var frequencyBandsProp = settingsType.GetProperty("FrequencyBands");
                if (frequencyBandsProp?.GetValue(settings) is int bands)
                {
                    frequencyBands = bands;
                    _logger.LogDebug("Updated frequency bands to {Bands}", bands);
                }

                // GainFactor - store for later use in overlay window configuration  
                double? gainFactor = null;
                var gainFactorProp = settingsType.GetProperty("GainFactor");
                if (gainFactorProp?.GetValue(settings) is double gain)
                {
                    gainFactor = gain;
                    _logger.LogDebug("Updated gain factor to {Gain}", gain);
                }

                // CRITICAL FIX: Thread-safe configuration update with proper synchronization
                lock (_lock)
                {
                    _configuration = overlayConfig;
                }
                
                // If overlay is currently active, force immediate update with safety checks
                if (_status == OverlayStatus.Active && _overlayWindow != null && !_disposed)
                {
                    try
                    {
                        // Check if window is still valid before any operations
                        if (_overlayWindow.IsDisposed || _overlayWindow.Disposing)
                        {
                            _logger.LogWarning("Overlay window is disposed or disposing, skipping configuration update");
                            return;
                        }

                        // Update the overlay window's internal configuration safely
                        UpdateOverlayWindowConfigurationSafe();
                        
                        // Update audio-specific settings (frequency bands and gain factor) with enhanced safety
                        if ((frequencyBands.HasValue || gainFactor.HasValue))
                        {
                            UpdateAudioSettingsSafe(frequencyBands, gainFactor);
                        }
                        
                        // Force immediate visual refresh of the spectrum display
                        ForceOverlayRefreshSafe();
                        
                        _logger.LogInformation("Taskbar spectrum settings updated and immediately refreshed");
                    }
                    catch (ObjectDisposedException)
                    {
                        _logger.LogWarning("Overlay window was disposed during settings update");
                        return;
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogWarning(ex, "Invalid operation during settings update, window may be disposed");
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error during settings update");
                        return;
                    }
                }
                else
                {
                    _logger.LogInformation("Taskbar spectrum settings updated (will apply when overlay becomes active)");
                }

                // Small delay to ensure all updates are processed
                await Task.Delay(1, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update TaskbarOverlayManager settings");
                throw;
            }
        }

        private async Task CreateOverlayWindowAsync()
        {
            if (_overlayWindow != null)
                return;

            var taskbarBounds = GetTaskbarBounds();
            var overlayBounds = CalculateOverlayBounds(taskbarBounds);

            _overlayWindow = new OverlayWindow(_logger, _iconRenderer, _configuration)
            {
                Bounds = overlayBounds
            };

            await _overlayWindow.InitializeAsync();
            
            // Force window to be visible and on top
            _overlayWindow.Show();
            _overlayWindow.BringToFront();
            _overlayWindow.Activate();
            
            // Ensure it stays on top
            NativeMethods.SetWindowPos(_overlayWindow.Handle, NativeMethods.HWND_TOPMOST, 
                overlayBounds.X, overlayBounds.Y, overlayBounds.Width, overlayBounds.Height, 
                NativeMethods.SWP_SHOWWINDOW);

            _logger.LogDebug("Overlay window created at {Bounds}", overlayBounds);
        }

        private async Task DestroyOverlayWindowAsync()
        {
            if (_overlayWindow == null)
                return;

            _overlayWindow.Hide();
            await _overlayWindow.DisposeAsync();
            _overlayWindow = null;

            _logger.LogDebug("Overlay window destroyed");
        }

        private async Task UpdateOverlayWindowAsync()
        {
            if (_overlayWindow == null)
                return;

            var taskbarBounds = GetTaskbarBounds();
            var overlayBounds = CalculateOverlayBounds(taskbarBounds);

            _overlayWindow.Bounds = overlayBounds;
            await _overlayWindow.UpdateConfigurationAsync(_configuration);
        }

        private void StartUpdateTimer()
        {
            var interval = Math.Max(16, 1000 / _configuration.UpdateFrequency); // Minimum 16ms for 60fps
            _updateTimer = new System.Threading.Timer(OnUpdateTimerTick, null, 0, interval);
        }

        private void StopUpdateTimer()
        {
            _updateTimer?.Dispose();
            _updateTimer = null;
        }

        private void OnUpdateTimerTick(object? state)
        {
            if (_disposed || _status != OverlayStatus.Active || _overlayWindow == null)
                return;

            try
            {
                var spectrumData = _latestSpectrumData;
                if (spectrumData != null)
                {
                    // Use async task to avoid blocking the timer thread
                    Task.Run(() =>
                    {
                        try
                        {
                            _overlayWindow.UpdateVisualization(spectrumData);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error updating visualization");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during overlay update");
            }
        }

        private Rectangle GetTaskbarBounds()
        {
            // Get the taskbar window
            var taskbarHandle = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (taskbarHandle == IntPtr.Zero)
            {
                // Fallback to screen bounds
                var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                return new Rectangle(0, screen.Bounds.Height - 40, screen.Bounds.Width, 40);
            }

            NativeMethods.GetWindowRect(taskbarHandle, out var rect);
            return new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        private Rectangle CalculateOverlayBounds(Rectangle taskbarBounds)
        {
            var width = _configuration.Width;
            var height = Math.Min(_configuration.Height, taskbarBounds.Height - (_configuration.Margin * 2));
            var x = _configuration.Position switch
            {
                OverlayPosition.Left => taskbarBounds.Left + _configuration.Margin,
                OverlayPosition.Right => taskbarBounds.Right - width - _configuration.Margin,
                OverlayPosition.Center => taskbarBounds.Left + (taskbarBounds.Width - width) / 2,
                _ => taskbarBounds.Left + (taskbarBounds.Width - width) / 2
            };
            var y = taskbarBounds.Top + (taskbarBounds.Height - height) / 2;

            return new Rectangle(x, y, width, height);
        }

        /// <summary>
        /// Forces immediate visual refresh of the taskbar overlay window.
        /// This ensures that configuration changes are immediately visible to the user.
        /// </summary>
        private void ForceOverlayRefresh()
        {
            if (_overlayWindow == null || _disposed)
                return;

            try
            {
                // Force the overlay window to repaint immediately with proper thread safety
                if (_overlayWindow.InvokeRequired)
                {
                    _overlayWindow.BeginInvoke(new Action(() =>
                    {
                        _overlayWindow.Invalidate(); // Mark entire window as needing repaint
                        _overlayWindow.Update();     // Force immediate repaint without waiting for message queue
                    }));
                }
                else
                {
                    _overlayWindow.Invalidate(); // Mark entire window as needing repaint
                    _overlayWindow.Update();     // Force immediate repaint without waiting for message queue
                }
                
                _logger.LogDebug("Forced immediate taskbar spectrum refresh");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to force overlay refresh");
            }
        }

        /// <summary>
        /// Updates the overlay window's internal configuration with thread safety.
        /// This ensures the window uses the latest settings for rendering operations.
        /// </summary>
        private void UpdateOverlayWindowConfiguration()
        {
            if (_overlayWindow == null || _disposed)
                return;

            try
            {
                // Update the overlay window's internal configuration using reflection
                var windowType = _overlayWindow.GetType();
                var updateConfigMethod = windowType.GetMethod("UpdateConfiguration");
                
                if (updateConfigMethod != null)
                {
                    // Ensure we're on the UI thread for window operations
                    if (_overlayWindow.InvokeRequired)
                    {
                        _overlayWindow.BeginInvoke(new Action(() => 
                            updateConfigMethod.Invoke(_overlayWindow, new object[] { _configuration })));
                    }
                    else
                    {
                        updateConfigMethod.Invoke(_overlayWindow, new object[] { _configuration });
                    }
                }
                
                _logger.LogDebug("Updated overlay window configuration");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update overlay window configuration");
            }
        }

        /// <summary>
        /// Thread-safe version of UpdateOverlayWindowConfiguration with enhanced disposal checks.
        /// </summary>
        private void UpdateOverlayWindowConfigurationSafe()
        {
            if (_overlayWindow == null || _disposed || _overlayWindow.IsDisposed || _overlayWindow.Disposing)
                return;

            try
            {
                // Get configuration safely with lock
                OverlayConfiguration currentConfig;
                lock (_lock)
                {
                    currentConfig = _configuration;
                }

                // Update the overlay window's configuration using the async method
                if (_overlayWindow.InvokeRequired)
                {
                    _overlayWindow.BeginInvoke(new Action(async () =>
                    {
                        try
                        {
                            if (!_overlayWindow.IsDisposed && !_overlayWindow.Disposing)
                            {
                                await _overlayWindow.UpdateConfigurationAsync(currentConfig);
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            _logger.LogDebug("Window disposed during configuration update");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error updating overlay configuration on UI thread");
                        }
                    }));
                }
                else
                {
                    // We're already on the UI thread, but still use async properly
                    Task.Run(async () =>
                    {
                        try
                        {
                            if (!_overlayWindow.IsDisposed && !_overlayWindow.Disposing)
                            {
                                await _overlayWindow.UpdateConfigurationAsync(currentConfig);
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            _logger.LogDebug("Window disposed during configuration update");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error updating overlay configuration");
                        }
                    });
                }
                
                _logger.LogDebug("Safely updated overlay window configuration");
            }
            catch (ObjectDisposedException)
            {
                _logger.LogDebug("Overlay window disposed during safe configuration update");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to safely update overlay window configuration");
            }
        }

        /// <summary>
        /// Thread-safe audio settings update with enhanced disposal checks.
        /// </summary>
        private void UpdateAudioSettingsSafe(int? frequencyBands, double? gainFactor)
        {
            if (_overlayWindow == null || _disposed || _overlayWindow.IsDisposed || _overlayWindow.Disposing)
                return;

            try
            {
                if (_overlayWindow.InvokeRequired)
                {
                    _overlayWindow.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (!_overlayWindow.IsDisposed && !_overlayWindow.Disposing)
                            {
                                _overlayWindow.UpdateAudioSettings(frequencyBands, gainFactor);
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            _logger.LogDebug("Window disposed during audio settings update");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error updating audio settings on UI thread");
                        }
                    }));
                }
                else
                {
                    if (!_overlayWindow.IsDisposed && !_overlayWindow.Disposing)
                    {
                        _overlayWindow.UpdateAudioSettings(frequencyBands, gainFactor);
                    }
                }
                
                _logger.LogDebug("Safely updated audio settings");
            }
            catch (ObjectDisposedException)
            {
                _logger.LogDebug("Overlay window disposed during safe audio settings update");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to safely update audio settings");
            }
        }

        /// <summary>
        /// Thread-safe overlay refresh with enhanced disposal checks.
        /// </summary>
        private void ForceOverlayRefreshSafe()
        {
            if (_overlayWindow == null || _disposed || _overlayWindow.IsDisposed || _overlayWindow.Disposing)
                return;

            try
            {
                if (_overlayWindow.InvokeRequired)
                {
                    _overlayWindow.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (!_overlayWindow.IsDisposed && !_overlayWindow.Disposing)
                            {
                                _overlayWindow.Invalidate();
                                _overlayWindow.Update();
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            _logger.LogDebug("Window disposed during refresh");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error refreshing overlay on UI thread");
                        }
                    }));
                }
                else
                {
                    if (!_overlayWindow.IsDisposed && !_overlayWindow.Disposing)
                    {
                        _overlayWindow.Invalidate();
                        _overlayWindow.Update();
                    }
                }
                
                _logger.LogDebug("Safely forced overlay refresh");
            }
            catch (ObjectDisposedException)
            {
                _logger.LogDebug("Overlay window disposed during safe refresh");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to safely refresh overlay");
            }
        }

        private void ChangeStatus(OverlayStatus newStatus, string reason)
        {
            var previousStatus = _status;
            _status = newStatus;
            
            _logger.LogDebug("Overlay status changed from {PreviousStatus} to {CurrentStatus}: {Reason}", 
                previousStatus, newStatus, reason);

            StatusChanged?.Invoke(this, new OverlayStatusChangedEventArgs(previousStatus, newStatus, reason));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                StopUpdateTimer();
                DestroyOverlayWindowAsync().GetAwaiter().GetResult();
                ChangeStatus(OverlayStatus.Disposed, "Manager disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during TaskbarOverlayManager disposal");
            }

            _logger.LogInformation("TaskbarOverlayManager disposed");
        }
    }

    /// <summary>
    /// Transparent overlay window for displaying audio visualization on taskbar.
    /// </summary>
    internal sealed class OverlayWindow : Form
    {
        private readonly ILogger _logger;
        private readonly IIconRenderer _iconRenderer;
        private OverlayConfiguration _configuration;
        private SpectrumDataEventArgs? _currentSpectrum;
        private float[] _smoothedSpectrum = new float[16]; // Start with default, will be resized based on settings
        private DateTime _lastAudioTime = DateTime.Now;
        private readonly float _decayRate = 0.92f; // Changed from 0.85f - faster decay
        
        // Settings from configuration
        private float _smoothingFactor = 0.8f; // Default
        private double _gainFactor = 1.0; // Default
        
        // Thread safety for configuration updates
        private readonly object _configLock = new object();
        
        // Add timer for consistent updates
        private System.Windows.Forms.Timer? _updateTimer;
        
        // Timer to ensure window stays on top
        private System.Windows.Forms.Timer? _topMostTimer;
        
        // Dragging and resizing support
        private bool _isDragging = false;
        private bool _isResizing = false;
        private ResizeDirection _resizeDirection = ResizeDirection.None;
        private Point _lastMousePos;
        private Point _resizeStartMousePos;
        private Size _resizeStartSize;
        
        // Performance optimization for dragging
        private DateTime _lastDragUpdate = DateTime.MinValue;
        private const int DRAG_UPDATE_INTERVAL_MS = 16; // ~60fps max
        
        private enum ResizeDirection
        {
            None,
            Right,
            Bottom,
            BottomRight
        }

        public OverlayWindow(ILogger logger, IIconRenderer iconRenderer, OverlayConfiguration configuration)
        {
            _logger = logger;
            _iconRenderer = iconRenderer;
            _configuration = configuration;

            SetupWindow();
        }

        private void SetupWindow()
        {
            // Make window transparent and resizable
            FormBorderStyle = FormBorderStyle.None; // We'll handle resize manually
            WindowState = FormWindowState.Normal;
            TopMost = true;
            ShowInTaskbar = false; // Hide from taskbar to keep it as overlay only
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.Manual;
            Visible = true; // Ensure it's visible
            
            // Set minimum and maximum sizes for sensible resizing
            MinimumSize = new Size(200, 40);
            MaximumSize = new Size(1200, 200);

            // Enable double buffering and mouse events
            SetStyle(ControlStyles.AllPaintingInWmPaint | 
                     ControlStyles.UserPaint | 
                     ControlStyles.DoubleBuffer | 
                     ControlStyles.ResizeRedraw, true);
            
            // Enable mouse events for dragging
            SetStyle(ControlStyles.UserMouse, true);

            // Set transparency for true overlay effect
            BackColor = Color.Magenta; // Use magenta as transparency key
            TransparencyKey = Color.Magenta; // Make magenta transparent
            Opacity = 1.0; // Full opacity for the bars themselves

            // Add timer for consistent refresh - throttled during drag operations
            _updateTimer = new System.Windows.Forms.Timer();
            _updateTimer.Interval = 16; // 60 FPS
            _updateTimer.Tick += (s, e) => {
                // Skip repaints during active drag/resize to improve performance
                if (!_isDragging && !_isResizing)
                {
                    Invalidate();
                }
            };
            _updateTimer.Start();
            
            // Add timer to ensure window stays on top (check every 2 seconds)
            _topMostTimer = new System.Windows.Forms.Timer();
            _topMostTimer.Interval = 2000; // 2 seconds
            _topMostTimer.Tick += (s, e) => {
                // Skip expensive top-most operations during drag/resize
                if (!_isDragging && !_isResizing)
                {
                    EnsureTopMost();
                }
            };
            _topMostTimer.Start();
            
            // Force window to stay on top
            TopMost = true;
            ShowInTaskbar = false;
            
            // Set extended window styles for always on top
            if (Handle != IntPtr.Zero)
            {
                var exStyle = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
                exStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_TOOLWINDOW;
                NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE, exStyle);
                
                // Force window to top
                NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, 
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            }
            
            // Enable key preview for global shortcuts
            KeyPreview = true;
        }

        public async Task InitializeAsync()
        {
            // Initialize renderer if needed
            await _iconRenderer.InitializeAsync(_configuration.RenderConfiguration);
        }

        public async Task UpdateConfigurationAsync(OverlayConfiguration configuration)
        {
            // CRITICAL FIX: Thread-safe configuration update with proper disposal checks
            if (IsDisposed || Disposing)
            {
                return;
            }

            try
            {
                // Thread-safe configuration update
                lock (_configLock)
                {
                    _configuration = configuration;
                }
                
                // UI thread operation for opacity change
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (!IsDisposed && !Disposing)
                            {
                                Opacity = configuration.Opacity;
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // Window disposed, ignore
                        }
                    }));
                }
                else
                {
                    if (!IsDisposed && !Disposing)
                    {
                        Opacity = configuration.Opacity;
                    }
                }
                
                // Update renderer configuration safely
                if (_iconRenderer != null)
                {
                    await _iconRenderer.UpdateConfigurationAsync(configuration.RenderConfiguration);
                }
            }
            catch (ObjectDisposedException)
            {
                // Window disposed, ignore
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error updating overlay window configuration");
                throw;
            }
        }

        public void UpdateVisualization(SpectrumDataEventArgs spectrumData)
        {
            _currentSpectrum = spectrumData;
            _lastAudioTime = DateTime.Now;
            
            if (spectrumData.Spectrum != null)
            {
                var targetLength = Math.Min(_smoothedSpectrum.Length, spectrumData.Spectrum.Length);
                
                for (int i = 0; i < targetLength; i++)
                {
                    var newValue = (float)spectrumData.Spectrum[i];
                    var currentValue = _smoothedSpectrum[i];
                    
                    // Use configurable smoothing factor like the main spectrum analyzer
                    _smoothedSpectrum[i] = _smoothedSpectrum[i] * _smoothingFactor + 
                                         newValue * (1f - _smoothingFactor);
                    
                    // Force to zero when very small
                    if (_smoothedSpectrum[i] < 0.001f)
                        _smoothedSpectrum[i] = 0;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                // Apply time-based decay when no audio
                var timeSinceLastAudio = (DateTime.Now - _lastAudioTime).TotalMilliseconds;
                if (timeSinceLastAudio > 100) // No audio for 100ms
                {
                    for (int i = 0; i < _smoothedSpectrum.Length; i++)
                    {
                        _smoothedSpectrum[i] *= _decayRate;
                        if (_smoothedSpectrum[i] < 0.001f)
                            _smoothedSpectrum[i] = 0;
                    }
                }
                
                // Render visualization directly to the overlay window
                using var bitmap = RenderVisualization(ClientSize);
                if (bitmap != null)
                {
                    e.Graphics.DrawImage(bitmap, 0, 0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error rendering overlay visualization");
            }
        }

        private Bitmap? RenderVisualization(Size size)
        {
            try
            {
                var bitmap = new Bitmap(size.Width, size.Height);
                using var graphics = Graphics.FromImage(bitmap);
                
                // Set high quality rendering
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.CompositingQuality = CompositingQuality.HighQuality;

                // Clear background
                graphics.Clear(Color.Transparent);

                // Render equalizer bars using smoothed data
                RenderEqualizerBars(graphics, size);

                return bitmap;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error creating visualization bitmap");
                return null;
            }
        }

        private void RenderEqualizerBars(Graphics graphics, Size size)
        {
            // Safety check for null spectrum
            if (_smoothedSpectrum == null || _smoothedSpectrum.Length == 0)
            {
                _logger.LogWarning("TASKBAR OVERLAY: Cannot render - spectrum array is null or empty");
                return;
            }

            // Thread-safe access to configuration
            OverlayConfiguration? currentConfig;
            float smoothingFactor;
            double gainFactor;
            
            lock (_configLock)
            {
                currentConfig = _configuration;
                smoothingFactor = _smoothingFactor;
                gainFactor = _gainFactor;
            }

            var barCount = _smoothedSpectrum.Length;
            var availableWidth = size.Width - 4; // Leave 2px margin on each side
            var barWidth = (float)availableWidth / barCount;
            var maxHeight = size.Height - 8; // Leave space for drag area
            
            DrawResizeIndicators(graphics, size, 8); // Show subtle resize grip

            // CRITICAL FIX: Use configuration colors instead of hardcoded values
            var colorScheme = currentConfig?.RenderConfiguration?.ColorScheme;
            Color primaryColor, secondaryColor;
            
            // Check if colors have been set to non-default values (indicating custom colors are being used)
            var defaultPrimary = Color.FromArgb(0, 120, 215);  // Windows 11 accent blue
            var defaultSecondary = Color.FromArgb(0, 90, 158); // Darker blue
            
            if (colorScheme != null && 
                colorScheme.PrimaryColor != Color.Empty && 
                colorScheme.SecondaryColor != Color.Empty &&
                (colorScheme.PrimaryColor != defaultPrimary || colorScheme.SecondaryColor != defaultSecondary))
            {
                // Use colors from configuration (these are the custom colors from settings)
                primaryColor = colorScheme.PrimaryColor;
                secondaryColor = colorScheme.SecondaryColor;
                _logger.LogDebug("TASKBAR OVERLAY: Using configured colors - Primary: {Primary}, Secondary: {Secondary}", 
                    primaryColor, secondaryColor);
            }
            else
            {
                // Use default blue theme colors (Windows 11 style)
                primaryColor = defaultPrimary;
                secondaryColor = defaultSecondary;
                _logger.LogDebug("TASKBAR OVERLAY: Using default colors - Primary: {Primary}, Secondary: {Secondary}", 
                    primaryColor, secondaryColor);
            }

            // Get visualization style from configuration
            var visualizationStyle = currentConfig?.RenderConfiguration?.Style ?? EqualizerStyle.Bars;
            
            _logger.LogDebug("TASKBAR OVERLAY: Rendering with style: {Style}", visualizationStyle);

            // Render based on style
            try
            {
                switch (visualizationStyle)
                {
                    case EqualizerStyle.Bars:
                        RenderBarsStyle(graphics, size, barCount, barWidth, maxHeight, primaryColor, secondaryColor, colorScheme);
                        break;
                    case EqualizerStyle.Dots:
                        RenderDotsStyle(graphics, size, barCount, barWidth, maxHeight, primaryColor, secondaryColor, colorScheme);
                        break;
                    case EqualizerStyle.Waveform:
                        RenderWaveformStyle(graphics, size, maxHeight, primaryColor, secondaryColor, colorScheme);
                        break;
                    case EqualizerStyle.Spectrum:
                        RenderSpectrumStyle(graphics, size, barCount, barWidth, maxHeight, primaryColor, secondaryColor, colorScheme);
                        break;
                    case EqualizerStyle.Lines:
                        RenderLinesStyle(graphics, size, barCount, barWidth, maxHeight, primaryColor, secondaryColor, colorScheme);
                        break;
                    default:
                        RenderBarsStyle(graphics, size, barCount, barWidth, maxHeight, primaryColor, secondaryColor, colorScheme);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rendering visualization style {Style}: {Message}", visualizationStyle, ex.Message);
                // Fall back to a simple safe rendering
                try
                {
                    using var fallbackBrush = new SolidBrush(primaryColor);
                    graphics.FillRectangle(fallbackBrush, 0, size.Height - 10, size.Width, 10);
                }
                catch
                {
                    // Even fallback failed, just ignore to prevent crash
                }
            }
            
            // Small visible drag handle for better UX
            using (var dotBrush = new SolidBrush(Color.FromArgb(64, 255, 255, 255))) // Semi-transparent
            {
                var centerX = size.Width / 2;
                graphics.FillEllipse(dotBrush, centerX - 6, 1, 2, 2);
                graphics.FillEllipse(dotBrush, centerX - 2, 1, 2, 2);
                graphics.FillEllipse(dotBrush, centerX + 2, 1, 2, 2);
                graphics.FillEllipse(dotBrush, centerX + 6, 1, 2, 2);
            }
        }
        
        private void DrawResizeIndicators(Graphics graphics, Size size, int resizeMargin)
        {
            // Small subtle resize indicator in bottom-right corner
            using (var resizeBrush = new SolidBrush(Color.FromArgb(48, 255, 255, 255))) // Very subtle
            {
                var cornerSize = 6;
                var x = size.Width - cornerSize - 2;
                var y = size.Height - cornerSize - 2;
                
                // Draw three small diagonal lines as resize grip
                using (var pen = new Pen(resizeBrush, 1))
                {
                    graphics.DrawLine(pen, x + 2, y + cornerSize - 1, x + cornerSize - 1, y + 2);
                    graphics.DrawLine(pen, x + 4, y + cornerSize - 1, x + cornerSize - 1, y + 4);
                    graphics.DrawLine(pen, x + 6, y + cornerSize - 1, x + cornerSize - 1, y + 6);
                }
            }
        }

        private void RenderBarsStyle(Graphics graphics, Size size, int barCount, float barWidth, int maxHeight, Color primaryColor, Color secondaryColor, ColorScheme? colorScheme)
        {
            // Create brush based on gradient settings
            Brush barBrush;
            if (colorScheme?.UseGradient == true)
            {
                // Create gradient brush with configured direction
                var gradientDirection = colorScheme.GradientDirection switch
                {
                    GradientDirection.Horizontal => LinearGradientMode.Horizontal,
                    GradientDirection.Diagonal => LinearGradientMode.BackwardDiagonal,
                    _ => LinearGradientMode.Vertical
                };
                
                var availableWidth = size.Width - 4;
                barBrush = new LinearGradientBrush(
                    new Rectangle(2, 4, availableWidth, maxHeight),
                    primaryColor,
                    secondaryColor,
                    gradientDirection);
            }
            else
            {
                // Use solid color (primary color only)
                barBrush = new SolidBrush(primaryColor);
            }

            using (barBrush)
            {
                // Draw bars with proper scaling
                for (int i = 0; i < barCount; i++)
                {
                    var level = Math.Max(0, Math.Min(1, _smoothedSpectrum[i] * (float)_gainFactor));
                    var barHeight = Math.Max(0, (int)(level * maxHeight));
                    var x = 2 + i * barWidth; // Start at 2px margin
                    var y = size.Height - barHeight - 2;
                    var width = Math.Max(1, barWidth - 0.5f); // Small gap between bars

                    // Draw main bar
                    if (barHeight > 0)
                    {
                        graphics.FillRectangle(barBrush, x, y, width, barHeight);
                    }
                }
            }
        }

        private void RenderDotsStyle(Graphics graphics, Size size, int barCount, float barWidth, int maxHeight, Color primaryColor, Color secondaryColor, ColorScheme? colorScheme)
        {
            var dotSize = Math.Max(2, Math.Min(6, barWidth * 0.6f)); // Dot size based on available width
            
            for (int i = 0; i < barCount; i++)
            {
                var level = Math.Max(0, Math.Min(1, _smoothedSpectrum[i] * (float)_gainFactor));
                var numDots = (int)(level * (maxHeight / (dotSize + 2))); // Number of dots based on level
                
                var x = 2 + i * barWidth + (barWidth - dotSize) / 2; // Center dot in bar space
                
                for (int d = 0; d < numDots; d++)
                {
                    var y = size.Height - 2 - (d * (dotSize + 2)) - dotSize;
                    
                    // Color interpolation for gradient effect
                    Color dotColor;
                    if (colorScheme?.UseGradient == true)
                    {
                        var ratio = (float)d / Math.Max(1, numDots - 1);
                        dotColor = InterpolateColor(primaryColor, secondaryColor, ratio);
                    }
                    else
                    {
                        dotColor = primaryColor;
                    }
                    
                    using var brush = new SolidBrush(dotColor);
                    graphics.FillEllipse(brush, x, y, dotSize, dotSize);
                }
            }
        }

        private void RenderWaveformStyle(Graphics graphics, Size size, int maxHeight, Color primaryColor, Color secondaryColor, ColorScheme? colorScheme)
        {
            if (_smoothedSpectrum.Length < 2) return;

            var points = new List<PointF>();
            var barCount = _smoothedSpectrum.Length;
            var stepX = barCount > 1 ? (float)(size.Width - 4) / (barCount - 1) : 0;

            // Create waveform points
            for (int i = 0; i < barCount; i++)
            {
                var level = Math.Max(0, Math.Min(1, _smoothedSpectrum[i] * (float)_gainFactor));
                var x = 2 + i * stepX;
                var y = size.Height - 2 - (level * maxHeight); // From bottom
                points.Add(new PointF(x, y));
            }

            // Draw the waveform
            if (points.Count >= 2)
            {
                var penWidth = Math.Max(1, size.Width / 200f); // Adaptive pen width
                using var pen = new Pen(primaryColor, penWidth);
                
                // Smooth curve if we have enough points
                if (points.Count >= 3)
                {
                    graphics.DrawCurve(pen, points.ToArray());
                }
                else
                {
                    graphics.DrawLines(pen, points.ToArray());
                }

                // Optional: fill under the curve
                if (colorScheme?.UseGradient == true)
                {
                    // Add baseline points for filling
                    var fillPoints = new List<PointF>(points);
                    fillPoints.Add(new PointF(size.Width - 2, size.Height - 2));
                    fillPoints.Add(new PointF(2, size.Height - 2));

                    using var fillBrush = new SolidBrush(Color.FromArgb(64, primaryColor)); // Semi-transparent
                    graphics.FillPolygon(fillBrush, fillPoints.ToArray());
                }
            }
        }

        private void RenderSpectrumStyle(Graphics graphics, Size size, int barCount, float barWidth, int maxHeight, Color primaryColor, Color secondaryColor, ColorScheme? colorScheme)
        {
            // Spectrum style: Similar to bars but with no gaps for continuous look
            Brush barBrush;
            if (colorScheme?.UseGradient == true)
            {
                var gradientDirection = colorScheme.GradientDirection switch
                {
                    GradientDirection.Horizontal => LinearGradientMode.Horizontal,
                    GradientDirection.Diagonal => LinearGradientMode.BackwardDiagonal,
                    _ => LinearGradientMode.Vertical
                };
                
                var availableWidth = size.Width - 4;
                barBrush = new LinearGradientBrush(
                    new Rectangle(2, 4, availableWidth, maxHeight),
                    primaryColor,
                    secondaryColor,
                    gradientDirection);
            }
            else
            {
                barBrush = new SolidBrush(primaryColor);
            }

            using (barBrush)
            {
                // Draw spectrum bars with no gaps for continuous look
                for (int i = 0; i < barCount; i++)
                {
                    var level = Math.Max(0, Math.Min(1, _smoothedSpectrum[i] * (float)_gainFactor));
                    var barHeight = Math.Max(0, (int)(level * maxHeight));
                    
                    if (barHeight > 0)
                    {
                        var x = 2 + i * barWidth;
                        var y = size.Height - barHeight - 2;
                        var width = barWidth; // No gaps between bars
                        
                        graphics.FillRectangle(barBrush, x, y, width, barHeight);
                    }
                }
            }
        }

        private void RenderLinesStyle(Graphics graphics, Size size, int barCount, float barWidth, int maxHeight, Color primaryColor, Color secondaryColor, ColorScheme? colorScheme)
        {
            // Lines style: Thin vertical lines instead of bars
            var penWidth = Math.Max(1, barWidth * 0.3f); // Thin lines
            
            for (int i = 0; i < barCount; i++)
            {
                var level = Math.Max(0, Math.Min(1, _smoothedSpectrum[i] * (float)_gainFactor));
                var lineHeight = (int)(level * maxHeight);
                
                if (lineHeight > 0)
                {
                    Color lineColor;
                    if (colorScheme?.UseGradient == true)
                    {
                        var ratio = (float)level;
                        lineColor = InterpolateColor(primaryColor, secondaryColor, ratio);
                    }
                    else
                    {
                        lineColor = primaryColor;
                    }
                    
                    using var pen = new Pen(lineColor, penWidth);
                    var x = 2 + i * barWidth + barWidth / 2; // Center the line
                    var y1 = size.Height - 2;
                    var y2 = y1 - lineHeight;
                    
                    graphics.DrawLine(pen, x, y1, x, y2);
                    
                    // Add dot at the top of each line for visual appeal (if space allows)
                    if (lineHeight > 5)
                    {
                        using var dotBrush = new SolidBrush(lineColor);
                        graphics.FillEllipse(dotBrush, x - 1, y2 - 1, 2, 2);
                    }
                }
            }
        }

        private static Color InterpolateColor(Color start, Color end, float ratio)
        {
            ratio = Math.Max(0, Math.Min(1, ratio));
            
            var r = (int)(start.R + (end.R - start.R) * ratio);
            var g = (int)(start.G + (end.G - start.G) * ratio);
            var b = (int)(start.B + (end.B - start.B) * ratio);
            var a = (int)(start.A + (end.A - start.A) * ratio);
            
            return Color.FromArgb(a, r, g, b);
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                _updateTimer?.Stop();
                _updateTimer?.Dispose();
                
                _topMostTimer?.Stop();
                _topMostTimer?.Dispose();
                
                if (InvokeRequired)
                {
                    Invoke(new Action(Close));
                }
                else
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing overlay window");
            }
            
            return ValueTask.CompletedTask;
        }
        
        // Mouse handling for dragging and resizing
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _resizeDirection = GetResizeDirection(e.Location);
                
                if (_resizeDirection != ResizeDirection.None)
                {
                    _isResizing = true;
                    // Capture the size and mouse position at the start of the resize
                    _resizeStartSize = Size;
                    _resizeStartMousePos = e.Location;
                }
                else
                {
                    _isDragging = true;
                }
                
                _lastMousePos = e.Location;
                Capture = true; // Capture mouse for better experience
            }
            else if (e.Button == MouseButtons.Right)
            {
                // Right-click for context menu - show overlay controls
                ShowOverlayContextMenu(e.Location);
            }
            base.OnMouseDown(e);
        }
        
        protected override void OnMouseMove(MouseEventArgs e)
        {
            // Throttle updates to prevent UI thread blocking
            var now = DateTime.Now;
            if ((now - _lastDragUpdate).TotalMilliseconds < DRAG_UPDATE_INTERVAL_MS)
            {
                return; // Skip this update to prevent overwhelming the UI thread
            }
            _lastDragUpdate = now;
            
            // Use Control.MouseButtons instead of e.Button for reliability
            if (_isResizing && Control.MouseButtons.HasFlag(MouseButtons.Left))
            {
                // Calculate deltas relative to the start of the resize
                var deltaX = e.Location.X - _resizeStartMousePos.X;
                var deltaY = e.Location.Y - _resizeStartMousePos.Y;

                int newWidth = Size.Width;
                int newHeight = Size.Height;

                switch (_resizeDirection)
                {
                    case ResizeDirection.Right:
                        newWidth = Math.Max(MinimumSize.Width,
                                          Math.Min(MaximumSize.Width, _resizeStartSize.Width + deltaX));
                        break;
                    case ResizeDirection.Bottom:
                        newHeight = Math.Max(MinimumSize.Height,
                                           Math.Min(MaximumSize.Height, _resizeStartSize.Height + deltaY));
                        break;
                    case ResizeDirection.BottomRight:
                        newWidth = Math.Max(MinimumSize.Width,
                                          Math.Min(MaximumSize.Width, _resizeStartSize.Width + deltaX));
                        newHeight = Math.Max(MinimumSize.Height,
                                           Math.Min(MaximumSize.Height, _resizeStartSize.Height + deltaY));
                        break;
                }

                // Use SuspendLayout/ResumeLayout for better performance during resize
                SuspendLayout();
                Size = new Size(newWidth, newHeight);
                ResumeLayout(false);
            }
            else if (_isDragging && Control.MouseButtons.HasFlag(MouseButtons.Left))
            {
                var deltaX = e.Location.X - _lastMousePos.X;
                var deltaY = e.Location.Y - _lastMousePos.Y;
                
                // Use SetBounds for more efficient position updates
                SetBounds(Location.X + deltaX, Location.Y + deltaY, Width, Height, BoundsSpecified.Location);
                
                // Update the last mouse pos so dragging follows the cursor
                _lastMousePos = e.Location;
            }
            else if (!_isDragging && !_isResizing)
            {
                // Only update cursor when not actively dragging/resizing
                UpdateCursor(e.Location);
            }
            base.OnMouseMove(e);
        }
        
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = false;
                _isResizing = false;
                _resizeDirection = ResizeDirection.None;
                Capture = false;
                
                // Reset drag throttling timer
                _lastDragUpdate = DateTime.MinValue;
                
                // Force a final update after drag ends to ensure UI is current
                Invalidate();
                
                UpdateCursor(e.Location); // Update cursor after release
            }
            base.OnMouseUp(e);
        }
        
        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
        }
        
        protected override void OnMouseLeave(EventArgs e)
        {
            if (!_isDragging && !_isResizing)
                Cursor = Cursors.Default;
            base.OnMouseLeave(e);
        }
        
        private ResizeDirection GetResizeDirection(Point mousePos)
        {
            const int resizeMargin = 8; // Pixels from edge to trigger resize
            
            bool nearRight = mousePos.X >= Width - resizeMargin;
            bool nearBottom = mousePos.Y >= Height - resizeMargin;
            
            if (nearRight && nearBottom)
                return ResizeDirection.BottomRight;
            else if (nearRight)
                return ResizeDirection.Right;
            else if (nearBottom)
                return ResizeDirection.Bottom;
            
            return ResizeDirection.None;
        }
        
        private void UpdateCursor(Point mousePos)
        {
            var direction = GetResizeDirection(mousePos);
            
            Cursor = direction switch
            {
                ResizeDirection.Right => Cursors.SizeWE,
                ResizeDirection.Bottom => Cursors.SizeNS,
                ResizeDirection.BottomRight => Cursors.SizeNWSE,
                _ => Cursors.SizeAll // Default drag cursor
            };
        }
        
        private void ShowOverlayContextMenu(Point location)
        {
            var contextMenu = new ContextMenuStrip();
            
            // Add menu items for overlay control
            contextMenu.Items.Add("Hide Overlay", null, (s, e) => Hide());
            contextMenu.Items.Add("Always On Top", null, (s, e) => ForceToTop());
            contextMenu.Items.Add("-"); // Separator
            
            // Size options
            var sizeMenu = new ToolStripMenuItem("Size Presets");
            sizeMenu.DropDownItems.Add("Compact (300x50)", null, (s, e) => Size = new Size(300, 50));
            sizeMenu.DropDownItems.Add("Normal (500x80)", null, (s, e) => Size = new Size(500, 80));
            sizeMenu.DropDownItems.Add("Wide (700x80)", null, (s, e) => Size = new Size(700, 80));
            sizeMenu.DropDownItems.Add("Large (800x120)", null, (s, e) => Size = new Size(800, 120));
            contextMenu.Items.Add(sizeMenu);
            
            contextMenu.Items.Add("-"); // Separator
            contextMenu.Items.Add("Reset Position", null, (s, e) => ResetPosition());
            
            contextMenu.Show(this, location);
        }
        
        private void ForceToTop()
        {
            // Force window back to top when it disappears
            TopMost = false; // Reset
            TopMost = true;  // Re-enable
            BringToFront();
            Activate();
            
            // Use Windows API to force it to top
            if (Handle != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 
                    0, 0, 0, 0, 
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            }
        }
        
        private void ResetPosition()
        {
            // Reset to center of primary screen
            var screen = Screen.PrimaryScreen;
            if (screen != null)
            {
                var centerX = (screen.Bounds.Width - Width) / 2;
                var centerY = screen.Bounds.Height - Height - 100; // Near bottom but not on taskbar
                Location = new Point(centerX, centerY);
            }
        }
        
        private void EnsureTopMost()
        {
            try
            {
                if (!IsDisposed && Handle != IntPtr.Zero && Visible)
                {
                    // Check if window is still topmost by checking if any window is above it
                    var topWindow = NativeMethods.GetTopWindow(IntPtr.Zero);
                    if (topWindow != Handle)
                    {
                        // If we're not the top window, force ourselves back to top
                        TopMost = false;
                        TopMost = true;
                        
                        // Use Windows API to ensure we're topmost
                        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST,
                            0, 0, 0, 0,
                            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error ensuring window stays on top");
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Handle Ctrl+Shift+E to bring overlay to front
            if (keyData == (Keys.Control | Keys.Shift | Keys.E))
            {
                ForceToTop();
                return true;
            }
            
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// Updates the overlay window's configuration with new settings.
        /// This method is called by the TaskbarOverlayManager when settings change.
        /// </summary>
        /// <param name="configuration">New overlay configuration with updated colors and styles</param>
        public void UpdateConfiguration(OverlayConfiguration configuration)
        {
            try
            {
                lock (_configLock)
                {
                    _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
                    
                    // Update smoothing factor from animation configuration
                    if (_configuration.RenderConfiguration?.Animation?.SmoothingFactor != null)
                    {
                        _smoothingFactor = (float)_configuration.RenderConfiguration.Animation.SmoothingFactor;
                        _logger.LogDebug("TASKBAR OVERLAY: Updated smoothing factor to {SmoothingFactor}", _smoothingFactor);
                    }
                }

                _logger.LogInformation("TASKBAR OVERLAY: Configuration updated - UseGradient: {UseGradient}, Primary: {Primary}, Secondary: {Secondary}", 
                    configuration.RenderConfiguration?.ColorScheme?.UseGradient,
                    configuration.RenderConfiguration?.ColorScheme?.PrimaryColor,
                    configuration.RenderConfiguration?.ColorScheme?.SecondaryColor);
                
                // Force immediate repaint to show new settings
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => {
                        Invalidate();
                        Update();
                    }));
                }
                else
                {
                    Invalidate();
                    Update();
                }
                
                _logger.LogDebug("TASKBAR OVERLAY: Forced immediate repaint with new configuration");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update overlay window configuration");
            }
        }

        /// <summary>
        /// Updates audio-specific settings for the overlay window.
        /// </summary>
        public void UpdateAudioSettings(int? frequencyBands, double? gainFactor)
        {
            try
            {
                lock (_configLock)
                {
                    // Update frequency bands if provided
                    if (frequencyBands.HasValue && frequencyBands.Value != _smoothedSpectrum.Length)
                    {
                        _logger.LogInformation("TASKBAR OVERLAY: Resizing spectrum array from {OldSize} to {NewSize}", 
                            _smoothedSpectrum.Length, frequencyBands.Value);
                        
                        var newSpectrum = new float[frequencyBands.Value];
                        
                        // Copy existing data if possible
                        var copyLength = Math.Min(_smoothedSpectrum.Length, newSpectrum.Length);
                        Array.Copy(_smoothedSpectrum, newSpectrum, copyLength);
                        
                        _smoothedSpectrum = newSpectrum;
                    }

                    // Update gain factor if provided
                    if (gainFactor.HasValue)
                    {
                        _gainFactor = gainFactor.Value;
                        _logger.LogDebug("TASKBAR OVERLAY: Updated gain factor to {GainFactor}", _gainFactor);
                    }

                    _logger.LogInformation("TASKBAR OVERLAY: Audio settings updated - Bands: {Bands}, Gain: {Gain}", 
                        _smoothedSpectrum.Length, _gainFactor);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update overlay window audio settings");
            }
        }
        
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_TOOLWINDOW;
                return cp;
            }
        }
    }

    /// <summary>
    /// Native Windows API methods for overlay window management.
    /// </summary>
    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        public static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_SHOWWINDOW = 0x0040;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}