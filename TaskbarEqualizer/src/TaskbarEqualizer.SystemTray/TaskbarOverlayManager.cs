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
        private float[] _peakHolds = new float[32];
        private DateTime[] _lastPeakTimes = new DateTime[32];
        private DateTime _lastAudioTime = DateTime.Now;
        private readonly float _decayRate = 0.85f; // Faster decay - bars fall down quicker
        private readonly float _smoothingFactor = 0.1f; // Minimal smoothing for maximum responsiveness
        
        // Dragging support
        private bool _isDragging = false;
        private Point _lastMousePos;

        public OverlayWindow(ILogger logger, IIconRenderer iconRenderer, OverlayConfiguration configuration)
        {
            _logger = logger;
            _iconRenderer = iconRenderer;
            _configuration = configuration;

            SetupWindow();
        }

        private void SetupWindow()
        {
            // Make window transparent and always on top
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            TopMost = true;
            ShowInTaskbar = false; // Hide from taskbar to keep it as overlay only
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.Manual;
            Visible = true; // Ensure it's visible

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

            // Make window layered but allow interaction for dragging
            if (Handle != IntPtr.Zero)
            {
                var exStyle = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
                exStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOPMOST;
                // Removed WS_EX_TRANSPARENT to allow mouse interaction for dragging
                NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE, exStyle);
            }
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
            
            // Process spectrum data for better responsiveness and decay
            if (spectrumData.Spectrum != null)
            {
                var now = DateTime.Now;
                var targetLength = Math.Min(_smoothedSpectrum.Length, spectrumData.Spectrum.Length);
                
                for (int i = 0; i < targetLength; i++)
                {
                    var newValue = (float)spectrumData.Spectrum[i];
                    
                    // For bars to fall properly, use immediate response for decreases
                    if (newValue > _smoothedSpectrum[i])
                    {
                        // Rising: use minimal smoothing
                        _smoothedSpectrum[i] = _smoothedSpectrum[i] * _smoothingFactor + 
                                              newValue * (1f - _smoothingFactor);
                    }
                    else
                    {
                        // Falling: use immediate response with decay
                        _smoothedSpectrum[i] = Math.Max(newValue, _smoothedSpectrum[i] * 0.8f);
                    }
                    
                    // Peak hold logic
                    if (_smoothedSpectrum[i] > _peakHolds[i])
                    {
                        _peakHolds[i] = _smoothedSpectrum[i];
                        _lastPeakTimes[i] = now;
                    }
                    else
                    {
                        // Decay peak holds after 500ms
                        if ((now - _lastPeakTimes[i]).TotalMilliseconds > 500)
                        {
                            _peakHolds[i] *= 0.9f;
                        }
                    }
                }
            }
            
            // Trigger repaint on UI thread
            if (InvokeRequired)
            {
                BeginInvoke(new Action(Invalidate));
            }
            else
            {
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                // Apply continuous decay for natural falling effect
                var timeSinceLastAudio = (DateTime.Now - _lastAudioTime).TotalMilliseconds;
                
                // Apply decay only when needed to prevent flickering
                bool hasAudio = timeSinceLastAudio < 100;
                bool hasSignificantLevel = false;
                
                for (int i = 0; i < _smoothedSpectrum.Length; i++)
                {
                    // Check if there's any significant signal
                    if (_smoothedSpectrum[i] > 0.001f || _peakHolds[i] > 0.001f)
                        hasSignificantLevel = true;
                        
                    // Only decay when there's something to decay
                    if (_smoothedSpectrum[i] > 0.001f)
                    {
                        _smoothedSpectrum[i] = Math.Max(0, _smoothedSpectrum[i] * _decayRate);
                        // Set to zero when very small to prevent flickering
                        if (_smoothedSpectrum[i] < 0.001f) _smoothedSpectrum[i] = 0;
                    }
                    
                    // Peak decay
                    if (_peakHolds[i] > 0.001f && timeSinceLastAudio > 50)
                    {
                        _peakHolds[i] = Math.Max(0, _peakHolds[i] * 0.95f);
                        if (_peakHolds[i] < 0.001f) _peakHolds[i] = 0;
                    }
                }
                
                // Stop repainting when no significant activity
                if (!hasAudio && !hasSignificantLevel && timeSinceLastAudio > 2000)
                {
                    // No need to keep repainting
                    return;
                }
                
                // Render visualization directly to the overlay window
                using var bitmap = RenderVisualization(ClientSize);
                if (bitmap != null)
                {
                    e.Graphics.DrawImage(bitmap, 0, 0);
                }
                
                // Schedule next repaint for smooth animation
                BeginInvoke(new Action(async () => {
                    await Task.Delay(16); // ~60 FPS for smooth animation
                    if (!IsDisposed)
                    {
                        Invalidate();
                    }
                }));
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
            var barWidth = (float)size.Width / barCount;
            var maxHeight = size.Height - 4; // Leave 2px margin top and bottom

            // Create gradient brush from green to red
            using var gradientBrush = new LinearGradientBrush(
                new Rectangle(0, 0, size.Width, size.Height),
                Color.FromArgb(0, 255, 100),    // Green at bottom
                Color.FromArgb(255, 100, 0),    // Red at top
                LinearGradientMode.Vertical);

            for (int i = 0; i < barCount; i++)
            {
                var level = Math.Max(0, Math.Min(1, _smoothedSpectrum[i]));
                var barHeight = Math.Max(1, (int)(level * maxHeight));
                var x = i * barWidth;
                var y = size.Height - barHeight - 2;
                var width = Math.Max(1, barWidth - 1);

                // Draw main bar
                if (barHeight > 1)
                {
                    graphics.FillRectangle(gradientBrush, x, y, width, barHeight);
                }

                // Draw peak hold line
                var peakLevel = Math.Max(0, Math.Min(1, _peakHolds[i]));
                if (peakLevel > 0.1f)
                {
                    var peakHeight = (int)(peakLevel * maxHeight);
                    var peakY = size.Height - peakHeight - 2;
                    using var peakPen = new Pen(Color.White, 1);
                    graphics.DrawLine(peakPen, x, peakY, x + width, peakY);
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            try
            {
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
        
        // Mouse handling for dragging
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _lastMousePos = e.Location;
                Cursor = Cursors.SizeAll;
            }
            base.OnMouseDown(e);
        }
        
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_isDragging && e.Button == MouseButtons.Left)
            {
                var deltaX = e.Location.X - _lastMousePos.X;
                var deltaY = e.Location.Y - _lastMousePos.Y;
                Location = new Point(Location.X + deltaX, Location.Y + deltaY);
            }
            base.OnMouseMove(e);
        }
        
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = false;
                Cursor = Cursors.Default;
            }
            base.OnMouseUp(e);
        }
        
        protected override void OnMouseEnter(EventArgs e)
        {
            Cursor = Cursors.SizeAll; // Show it's draggable
            base.OnMouseEnter(e);
        }
        
        protected override void OnMouseLeave(EventArgs e)
        {
            if (!_isDragging)
                Cursor = Cursors.Default;
            base.OnMouseLeave(e);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // Make window layered but allow interaction for dragging
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOPMOST;
                // Removed WS_EX_TRANSPARENT to allow mouse interaction
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

        [DllImport("user32.dll")]
        public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

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