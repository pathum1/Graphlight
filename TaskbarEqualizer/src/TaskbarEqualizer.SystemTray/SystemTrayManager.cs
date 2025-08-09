using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.SystemTray
{
    /// <summary>
    /// Windows system tray integration and management implementation.
    /// Provides taskbar icon display and user interaction capabilities.
    /// </summary>
    public sealed class SystemTrayManager : ISystemTrayManager
    {
        private readonly ILogger<SystemTrayManager> _logger;
        private readonly object _trayLock = new();
        
        private NotifyIcon? _notifyIcon;
        private Icon? _currentIcon;
        private string _toolTipText = string.Empty;
        
        private bool _isVisible;
        private bool _isInitialized;
        private bool _contextMenuEnabled = true;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the SystemTrayManager.
        /// </summary>
        /// <param name="logger">Logger for diagnostic information.</param>
        public SystemTrayManager(ILogger<SystemTrayManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogDebug("SystemTrayManager created");
        }

        #region Events

        /// <inheritdoc />
        public event EventHandler<TrayIconClickedEventArgs>? TrayIconClicked;

        /// <inheritdoc />
        public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;

        /// <inheritdoc />
#pragma warning disable CS0067 // Event is never used - reserved for future implementation
        public event EventHandler<EventArgs>? ExitRequested;
#pragma warning restore CS0067

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion

        #region Properties

        /// <inheritdoc />
        public bool IsVisible => _isVisible;

        /// <inheritdoc />
        public Icon? CurrentIcon => _currentIcon;

        /// <inheritdoc />
        public string ToolTipText => _toolTipText;

        /// <inheritdoc />
        public bool IsInitialized => _isInitialized;

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public async Task InitializeAsync(Icon initialIcon, string toolTipText, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SystemTrayManager));

            if (_isInitialized)
            {
                _logger.LogWarning("SystemTrayManager is already initialized");
                return;
            }

            if (initialIcon == null)
                throw new ArgumentNullException(nameof(initialIcon));

            _logger.LogInformation("Initializing system tray manager with tooltip: {ToolTip}", toolTipText);
            _logger.LogInformation("🔧 DIAGNOSTIC: Current thread apartment state: {ApartmentState}", Thread.CurrentThread.GetApartmentState());
            _logger.LogInformation("🔧 DIAGNOSTIC: Current synchronization context: {SyncContext}", SynchronizationContext.Current?.GetType().Name ?? "null");

            try
            {
                // FORCE creation on UI thread with STA apartment state
                var tcs = new TaskCompletionSource<bool>();
                
                // Create a dedicated STA thread for NotifyIcon
                var staThread = new Thread(() =>
                {
                    try
                    {
                        _logger.LogInformation("🔧 DIAGNOSTIC: Creating NotifyIcon on dedicated STA thread");
                        
                        // Set up Windows Forms application context on this thread
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                        
                        lock (_trayLock)
                        {
                            CreateNotifyIcon(initialIcon, toolTipText);
                            _isInitialized = true;
                        }
                        
                        _logger.LogInformation("🔧 DIAGNOSTIC: NotifyIcon created successfully on STA thread");
                        tcs.SetResult(true);
                        
                        // Keep the thread alive for NotifyIcon message processing
                        Application.Run();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "🔧 DIAGNOSTIC: Failed to create NotifyIcon on STA thread");
                        tcs.SetException(ex);
                    }
                });
                
                staThread.SetApartmentState(ApartmentState.STA);
                staThread.IsBackground = true;
                staThread.Name = "NotifyIcon STA Thread";
                staThread.Start();
                
                await tcs.Task;
                _logger.LogDebug("SystemTrayManager initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize SystemTrayManager");
                throw;
            }
        }

        /// <inheritdoc />
        public async Task ShowAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SystemTrayManager));

            if (!_isInitialized)
                throw new InvalidOperationException("SystemTrayManager must be initialized before showing");

            if (_isVisible)
            {
                _logger.LogDebug("Tray icon is already visible");
                return;
            }

            _logger.LogDebug("Showing tray icon");

            try
            {
                await Task.Run(() =>
                {
                    lock (_trayLock)
                    {
                        if (_notifyIcon != null)
                        {
                            _logger.LogInformation("🔧 DIAGNOSTIC: Setting NotifyIcon.Visible = true");
                            _notifyIcon.Visible = true;
                            _isVisible = true;
                            OnPropertyChanged(nameof(IsVisible));
                            _logger.LogInformation("🔧 DIAGNOSTIC: NotifyIcon is now visible, ready for mouse events");
                        }
                        else
                        {
                            _logger.LogError("🔧 DIAGNOSTIC: Cannot show tray icon - NotifyIcon is null!");
                        }
                    }
                }, cancellationToken);

                _logger.LogDebug("Tray icon shown successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show tray icon");
                throw;
            }
        }

        /// <inheritdoc />
        public async Task HideAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SystemTrayManager));

            if (!_isVisible)
            {
                _logger.LogDebug("Tray icon is already hidden");
                return;
            }

            _logger.LogDebug("Hiding tray icon");

            try
            {
                await Task.Run(() =>
                {
                    lock (_trayLock)
                    {
                        if (_notifyIcon != null)
                        {
                            _notifyIcon.Visible = false;
                            _isVisible = false;
                            OnPropertyChanged(nameof(IsVisible));
                        }
                    }
                }, cancellationToken);

                _logger.LogDebug("Tray icon hidden successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to hide tray icon");
                throw;
            }
        }

        /// <inheritdoc />
        public async Task UpdateIconAsync(Icon newIcon, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SystemTrayManager));

            if (newIcon == null)
                throw new ArgumentNullException(nameof(newIcon));

            try
            {
                await Task.Run(() => UpdateIconInternal(newIcon), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update tray icon asynchronously");
                throw;
            }
        }

        /// <inheritdoc />
        public void UpdateIcon(Icon newIcon)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SystemTrayManager));

            if (newIcon == null)
                throw new ArgumentNullException(nameof(newIcon));

            try
            {
                UpdateIconInternal(newIcon);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update tray icon synchronously");
                throw;
            }
        }

        /// <inheritdoc />
        public async Task UpdateToolTipAsync(string text, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SystemTrayManager));

            if (string.IsNullOrEmpty(text))
                text = string.Empty;

            if (_toolTipText == text)
                return;

            _logger.LogDebug("Updating tooltip to: {ToolTip}", text);

            try
            {
                await Task.Run(() =>
                {
                    lock (_trayLock)
                    {
                        if (_notifyIcon != null)
                        {
                            // NotifyIcon has a 127 character limit for tooltips
                            var truncatedText = text.Length > 127 ? text.Substring(0, 127) : text;
                            _notifyIcon.Text = truncatedText;
                            _toolTipText = text;
                            OnPropertyChanged(nameof(ToolTipText));
                        }
                    }
                }, cancellationToken);

                _logger.LogDebug("Tooltip updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update tooltip");
                throw;
            }
        }

        /// <inheritdoc />
        public async Task ShowBalloonTipAsync(string title, string text, BalloonTipIcon icon = BalloonTipIcon.Info, 
            int timeout = 3000, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SystemTrayManager));

            if (!_isInitialized || !_isVisible)
            {
                _logger.LogWarning("Cannot show balloon tip - tray icon not initialized or visible");
                return;
            }

            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(text))
                return;

            _logger.LogDebug("Showing balloon tip: {Title} - {Text}", title, text);

            try
            {
                await Task.Run(() =>
                {
                    lock (_trayLock)
                    {
                        if (_notifyIcon != null)
                        {
                            var toolTipIcon = icon switch
                            {
                                BalloonTipIcon.Info => ToolTipIcon.Info,
                                BalloonTipIcon.Warning => ToolTipIcon.Warning,
                                BalloonTipIcon.Error => ToolTipIcon.Error,
                                _ => ToolTipIcon.None
                            };

                            _notifyIcon.ShowBalloonTip(timeout, title ?? string.Empty, text ?? string.Empty, toolTipIcon);
                        }
                    }
                }, cancellationToken);

                _logger.LogDebug("Balloon tip shown successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show balloon tip");
                throw;
            }
        }

        /// <inheritdoc />
        public void SetContextMenuEnabled(bool enabled)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SystemTrayManager));

            if (_contextMenuEnabled == enabled)
                return;

            _logger.LogDebug("Setting context menu enabled: {Enabled}", enabled);

            lock (_trayLock)
            {
                _contextMenuEnabled = enabled;
                
                // Context menu handling is done in the event handlers
                // The enabled state is checked there
            }
        }

        /// <summary>
        /// Sets the context menu strip for the tray icon.
        /// </summary>
        /// <param name="contextMenuStrip">Context menu strip to assign.</param>
        public void SetContextMenuStrip(ContextMenuStrip contextMenuStrip)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SystemTrayManager));

            lock (_trayLock)
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.ContextMenuStrip = contextMenuStrip;
                    _logger.LogDebug("Context menu strip assigned to notify icon");
                }
            }
        }

        /// <inheritdoc />
        public Point GetTrayIconPosition()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SystemTrayManager));

            if (!_isInitialized || !_isVisible)
                return Point.Empty;

            try
            {
                // Try to get the tray icon position using Windows API
                var trayHandle = FindTrayIcon();
                if (trayHandle != IntPtr.Zero)
                {
                    if (GetWindowRect(trayHandle, out var rect))
                    {
                        return new Point(rect.Left, rect.Top);
                    }
                }

                // Fallback: estimate position at bottom-right of screen
                var screen = Screen.PrimaryScreen;
                if (screen != null)
                {
                    return new Point(screen.WorkingArea.Right - 50, screen.WorkingArea.Bottom - 50);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get tray icon position");
            }

            return Point.Empty;
        }

        /// <inheritdoc />
        public async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SystemTrayManager));

            if (!_isInitialized)
                return;

            _logger.LogDebug("Refreshing tray icon");

            try
            {
                await Task.Run(() =>
                {
                    lock (_trayLock)
                    {
                        if (_notifyIcon != null && _isVisible)
                        {
                            // Force refresh by toggling visibility
                            _notifyIcon.Visible = false;
                            Thread.Sleep(10); // Brief pause
                            _notifyIcon.Visible = true;
                        }
                    }
                }, cancellationToken);

                _logger.LogDebug("Tray icon refreshed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh tray icon");
                throw;
            }
        }

        #endregion

        #region Private Methods

        private void CreateNotifyIcon(Icon initialIcon, string toolTipText)
        {
            _logger.LogInformation("🔧 DIAGNOSTIC: Creating NotifyIcon with tooltip: {ToolTip}", toolTipText);
            
            _notifyIcon = new NotifyIcon
            {
                Icon = initialIcon,
                Text = toolTipText.Length > 127 ? toolTipText.Substring(0, 127) : toolTipText,
                Visible = false
            };

            _logger.LogInformation("🔧 DIAGNOSTIC: NotifyIcon created - Handle: {Handle}", _notifyIcon.GetType().Name);

            // Store references
            _currentIcon = initialIcon;
            _toolTipText = toolTipText;

            // Wire up event handlers
            _logger.LogInformation("🔧 DIAGNOSTIC: Wiring up NotifyIcon event handlers...");
            
            // Try MouseClick first to see if ANY mouse events work
            _notifyIcon.MouseClick += OnNotifyIconMouseClick_MainHandler;
            _logger.LogInformation("🔧 DIAGNOSTIC: MouseClick main handler registered");
            
            // Keep MouseUp as backup
            _notifyIcon.MouseUp += OnNotifyIconMouseUp;
            _logger.LogInformation("🔧 DIAGNOSTIC: MouseUp event handler registered");
            
            _notifyIcon.MouseDoubleClick += OnNotifyIconMouseDoubleClick;
            _logger.LogInformation("🔧 DIAGNOSTIC: MouseDoubleClick event handler registered");
            
            _notifyIcon.BalloonTipClicked += OnBalloonTipClicked;
            _notifyIcon.BalloonTipClosed += OnBalloonTipClosed;
            
            _logger.LogInformation("🔧 DIAGNOSTIC: All NotifyIcon event handlers registered successfully");

            // Also try MouseDown for even more comprehensive testing
            _notifyIcon.MouseDown += OnNotifyIconMouseDown_Diagnostic;
            _logger.LogInformation("🔧 DIAGNOSTIC: MouseDown diagnostic handler also registered");
        }

        private void UpdateIconInternal(Icon newIcon)
        {
            lock (_trayLock)
            {
                if (_notifyIcon != null && _currentIcon != newIcon)
                {
                    _notifyIcon.Icon = newIcon;
                    _currentIcon = newIcon;
                    OnPropertyChanged(nameof(CurrentIcon));
                }
            }
        }

        private void OnNotifyIconMouseClick_MainHandler(object? sender, MouseEventArgs e)
        {
            _logger.LogInformation("🖱️ DIAGNOSTIC: MouseClick MAIN event fired - Button: {Button}, Location: ({X}, {Y})", 
                e.Button, e.X, e.Y);
                
            try
            {
                var button = e.Button switch
                {
                    MouseButtons.Left => TrayMouseButton.Left,
                    MouseButtons.Right => TrayMouseButton.Right,
                    MouseButtons.Middle => TrayMouseButton.Middle,
                    _ => TrayMouseButton.Left
                };

                _logger.LogInformation("🖱️ DIAGNOSTIC: MouseClick mapped to: {MappedButton}", button);

                // fire context menu on right button click
                if (button == TrayMouseButton.Right && _contextMenuEnabled)
                {
                    var cursorPos = Cursor.Position;
                    _logger.LogInformation("🖱️ DIAGNOSTIC: Right button detected in MouseClick - firing ContextMenuRequested at {Position}", cursorPos);
                    
                    var menuArgs = new ContextMenuRequestedEventArgs(cursorPos);
                    _logger.LogInformation("🖱️ DIAGNOSTIC: MouseClick ContextMenuRequested event has {SubscriberCount} subscribers", 
                        ContextMenuRequested?.GetInvocationList()?.Length ?? 0);
                    
                    ContextMenuRequested?.Invoke(this, menuArgs);
                    _logger.LogInformation("🖱️ DIAGNOSTIC: MouseClick ContextMenuRequested event fired successfully");
                }
                else
                {
                    _logger.LogInformation("🖱️ DIAGNOSTIC: MouseClick not showing context menu - Button: {Button}, ContextMenuEnabled: {Enabled}", 
                        button, _contextMenuEnabled);
                }

                // still fire the click event
                TrayIconClicked?.Invoke(this, new TrayIconClickedEventArgs(button, 1, new Point(e.X, e.Y)));
                _logger.LogDebug("🖱️ DIAGNOSTIC: MouseClick TrayIconClicked event fired for button: {Button}", button);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🖱️ DIAGNOSTIC: Error handling tray icon mouse click");
            }
        }

        private void OnNotifyIconMouseDown_Diagnostic(object? sender, MouseEventArgs e)
        {
            _logger.LogInformation("🖱️ DIAGNOSTIC: MouseDown event fired - Button: {Button}, Location: ({X}, {Y})", 
                e.Button, e.X, e.Y);
        }

        private void OnNotifyIconMouseUp(object? sender, MouseEventArgs e)
        {
            try
            {
                _logger.LogInformation("🖱️ DIAGNOSTIC: NotifyIcon MouseUp fired - Button: {Button}, Location: ({X}, {Y}), ContextMenuEnabled: {Enabled}", 
                    e.Button, e.X, e.Y, _contextMenuEnabled);

                var button = e.Button switch
                {
                    MouseButtons.Left => TrayMouseButton.Left,
                    MouseButtons.Right => TrayMouseButton.Right,
                    MouseButtons.Middle => TrayMouseButton.Middle,
                    _ => TrayMouseButton.Left
                };

                _logger.LogInformation("🖱️ DIAGNOSTIC: Mapped button to: {MappedButton}", button);

                // fire context menu only on right button release
                if (button == TrayMouseButton.Right && _contextMenuEnabled)
                {
                    var cursorPos = Cursor.Position;
                    _logger.LogInformation("🖱️ DIAGNOSTIC: Right button detected - firing ContextMenuRequested at {Position}", cursorPos);
                    
                    var menuArgs = new ContextMenuRequestedEventArgs(cursorPos);
                    _logger.LogInformation("🖱️ DIAGNOSTIC: ContextMenuRequested event has {SubscriberCount} subscribers", 
                        ContextMenuRequested?.GetInvocationList()?.Length ?? 0);
                    
                    ContextMenuRequested?.Invoke(this, menuArgs);
                    _logger.LogInformation("🖱️ DIAGNOSTIC: ContextMenuRequested event fired successfully");
                }
                else
                {
                    _logger.LogInformation("🖱️ DIAGNOSTIC: Not showing context menu - Button: {Button}, ContextMenuEnabled: {Enabled}", 
                        button, _contextMenuEnabled);
                }

                // still fire the click event
                TrayIconClicked?.Invoke(this, new TrayIconClickedEventArgs(button, 1, new Point(e.X, e.Y)));
                _logger.LogDebug("🖱️ DIAGNOSTIC: TrayIconClicked event fired for button: {Button}", button);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🖱️ DIAGNOSTIC: Error handling tray icon mouse up");
            }
        }

        private void OnNotifyIconMouseDoubleClick(object? sender, MouseEventArgs e)
        {
            try
            {
                var button = e.Button switch
                {
                    MouseButtons.Left => TrayMouseButton.Left,
                    MouseButtons.Right => TrayMouseButton.Right,
                    MouseButtons.Middle => TrayMouseButton.Middle,
                    _ => TrayMouseButton.Left
                };

                var args = new TrayIconClickedEventArgs(button, 2, new Point(e.X, e.Y));
                TrayIconClicked?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling tray icon double-click");
            }
        }

        private void OnBalloonTipClicked(object? sender, EventArgs e)
        {
            _logger.LogDebug("Balloon tip clicked");
        }

        private void OnBalloonTipClosed(object? sender, EventArgs e)
        {
            _logger.LogDebug("Balloon tip closed");
        }

        private IntPtr FindTrayIcon()
        {
            // Try to find the notification area
            var taskbarHandle = FindWindow("Shell_TrayWnd", null);
            if (taskbarHandle != IntPtr.Zero)
            {
                var trayHandle = FindWindowEx(taskbarHandle, IntPtr.Zero, "TrayNotifyWnd", null);
                if (trayHandle != IntPtr.Zero)
                {
                    return FindWindowEx(trayHandle, IntPtr.Zero, "SysPager", null);
                }
            }

            return IntPtr.Zero;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Windows API

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the system tray manager and cleans up resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                lock (_trayLock)
                {
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Visible = false;
                        _notifyIcon.Dispose();
                        _notifyIcon = null;
                    }

                    _currentIcon = null;
                    _isVisible = false;
                    _isInitialized = false;
                }

                _logger.LogDebug("SystemTrayManager disposed");
            }
        }

        #endregion
    }
}