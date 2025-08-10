using System;
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

                // Update overlay configuration properties
                var overlayConfig = new OverlayConfiguration
                {
                    Enabled = _configuration.Enabled,
                    Position = _configuration.Position,
                    Width = _configuration.Width,
                    Height = _configuration.Height,
                    Margin = _configuration.Margin,
                    AutoHide = _configuration.AutoHide,
                    AutoHideDelay = _configuration.AutoHideDelay,
                    RenderConfiguration = new RenderConfiguration()
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
                    // Animation is controlled by the animation configuration
                    if (renderConfig.Animation == null)
                        renderConfig.Animation = new AnimationConfiguration();
                    
                    _logger.LogDebug("Animations enabled: {Enabled}", enableAnimations);
                }

                // EnableEffects
                var enableEffectsProp = settingsType.GetProperty("EnableEffects");
                if (enableEffectsProp?.GetValue(settings) is bool enableEffects)
                {
                    renderConfig.EnableEffects = enableEffects;
                    _logger.LogDebug("Updated effects enabled to {Enabled}", enableEffects);
                }

                // AntiAliasing (defaults to enabled)
                renderConfig.AntiAliasing = true;

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

                // Update color scheme
                if (renderConfig.ColorScheme == null)
                    renderConfig.ColorScheme = new ColorScheme();

                var colorScheme = renderConfig.ColorScheme;

                // UseCustomColors and custom colors
                var useCustomColorsProp = settingsType.GetProperty("UseCustomColors");
                var useCustomColors = useCustomColorsProp?.GetValue(settings) is bool customColors && customColors;

                if (useCustomColors)
                {
                    var primaryColorProp = settingsType.GetProperty("CustomPrimaryColor");
                    if (primaryColorProp?.GetValue(settings) is System.Drawing.Color primaryColor)
                    {
                        colorScheme.PrimaryColor = primaryColor;
                        _logger.LogDebug("Updated primary color to {Color}", primaryColor);
                    }

                    var secondaryColorProp = settingsType.GetProperty("CustomSecondaryColor");
                    if (secondaryColorProp?.GetValue(settings) is System.Drawing.Color secondaryColor)
                    {
                        colorScheme.SecondaryColor = secondaryColor;
                        _logger.LogDebug("Updated secondary color to {Color}", secondaryColor);
                    }
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

                // Apply the updated configuration
                await UpdateConfigurationAsync(overlayConfig, cancellationToken);

                _logger.LogInformation("TaskbarOverlayManager settings updated successfully");
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
        private float[] _smoothedSpectrum = new float[32];
        private DateTime _lastAudioTime = DateTime.Now;
        private readonly float _decayRate = 0.92f; // Changed from 0.85f - faster decay
        
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
            _configuration = configuration;
            Opacity = configuration.Opacity;
            
            // Update renderer configuration
            await _iconRenderer.UpdateConfigurationAsync(configuration.RenderConfiguration);
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
                    
                    // Proper smoothing logic
                    if (newValue > currentValue)
                    {
                        // Rising: fast attack
                        _smoothedSpectrum[i] = currentValue + (newValue - currentValue) * 0.7f;
                    }
                    else
                    {
                        // Falling: smooth decay
                        _smoothedSpectrum[i] = currentValue * 0.3f + newValue * 0.7f;
                    }
                    
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
            var barCount = _smoothedSpectrum.Length;
            var availableWidth = size.Width - 4; // Leave 2px margin on each side
            var barWidth = (float)availableWidth / barCount;
            var maxHeight = size.Height - 8; // Leave space for drag area
            
            // Border made transparent - functionality remains
            // using (var borderPen = new Pen(Color.FromArgb(0, 255, 255, 255), 1))
            // {
            //     graphics.DrawRectangle(borderPen, 0, 0, size.Width - 1, size.Height - 1);
            // }
            
            // Drag area made transparent - dragging still works via mouse events
            // using (var dragBrush = new SolidBrush(Color.FromArgb(0, 255, 255, 255)))
            // {
            //     graphics.FillRectangle(dragBrush, 0, 0, size.Width, 4);
            // }

            DrawResizeIndicators(graphics, size, 8); // Show subtle resize grip

            // Create gradient brush from green to red
            using var gradientBrush = new LinearGradientBrush(
                new Rectangle(2, 4, availableWidth, maxHeight),
                Color.FromArgb(0, 255, 100),    // Green at bottom
                Color.FromArgb(255, 100, 0),    // Red at top
                LinearGradientMode.Vertical);

            // Draw bars with proper scaling
            for (int i = 0; i < barCount; i++)
            {
                var level = Math.Max(0, Math.Min(1, _smoothedSpectrum[i]));
                var barHeight = Math.Max(0, (int)(level * maxHeight));
                var x = 2 + i * barWidth; // Start at 2px margin
                var y = size.Height - barHeight - 2;
                var width = Math.Max(1, barWidth - 0.5f); // Small gap between bars

                // Draw main bar
                if (barHeight > 0)
                {
                    graphics.FillRectangle(gradientBrush, x, y, width, barHeight);
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