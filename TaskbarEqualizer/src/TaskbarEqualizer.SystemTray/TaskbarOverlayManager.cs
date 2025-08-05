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
            _overlayWindow.Show();

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
            var interval = 1000 / _configuration.UpdateFrequency;
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
                    _overlayWindow.UpdateVisualization(spectrumData);
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
            ShowInTaskbar = false;
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.Manual;

            // Enable double buffering
            SetStyle(ControlStyles.AllPaintingInWmPaint | 
                     ControlStyles.UserPaint | 
                     ControlStyles.DoubleBuffer | 
                     ControlStyles.ResizeRedraw, true);

            // Set transparency
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            Opacity = _configuration.Opacity;

            // Make window click-through
            var exStyle = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
            exStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT;
            NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE, exStyle);
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
            if (_currentSpectrum == null)
                return;

            try
            {
                // Render visualization directly to the overlay window
                using var bitmap = RenderVisualization(_currentSpectrum, ClientSize);
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

        private Bitmap? RenderVisualization(SpectrumDataEventArgs spectrum, Size size)
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

                // Render equalizer bars
                RenderEqualizerBars(graphics, spectrum, size);

                return bitmap;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error creating visualization bitmap");
                return null;
            }
        }

        private void RenderEqualizerBars(Graphics graphics, SpectrumDataEventArgs spectrum, Size size)
        {
            if (spectrum.Spectrum == null || spectrum.Spectrum.Length == 0)
                return;

            var barCount = Math.Min(spectrum.Spectrum.Length, 32); // Limit to 32 bars for taskbar
            var barWidth = (float)size.Width / barCount;
            var maxHeight = size.Height - 4; // Leave 2px margin top and bottom

            using var brush = new SolidBrush(_configuration.RenderConfiguration.ColorScheme.PrimaryColor);

            for (int i = 0; i < barCount; i++)
            {
                var level = spectrum.Spectrum[i];
                var barHeight = Math.Max(1, (int)(level * maxHeight));
                var x = i * barWidth;
                var y = size.Height - barHeight - 2;

                graphics.FillRectangle(brush, x, y, barWidth - 1, barHeight);
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

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // Make window layered and transparent to clicks
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT;
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

        [DllImport("user32.dll")]
        public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

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