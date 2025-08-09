using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Configuration.Services;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.Main
{
    /// <summary>
    /// Application context for TaskbarEqualizer that manages the system tray lifecycle.
    /// </summary>
    public class TaskbarEqualizerApplicationContext : ApplicationContext
    {
        private readonly ISystemTrayManager _systemTrayManager;
        private readonly ApplicationOrchestrator _orchestrator;
        private readonly ILogger _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly bool _isMinimized;
        private Timer _initTimer;
        private IContextMenuManager? _contextMenuManager;

        public TaskbarEqualizerApplicationContext(
            ISystemTrayManager systemTrayManager,
            ApplicationOrchestrator orchestrator,
            ILogger logger,
            IServiceProvider serviceProvider,
            bool isMinimized)
        {
            _systemTrayManager = systemTrayManager ?? throw new ArgumentNullException(nameof(systemTrayManager));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _isMinimized = isMinimized;

            // Initialize with a timer to ensure we're on the UI thread
            _initTimer = new Timer();
            _initTimer.Interval = 100; // 100ms delay
            _initTimer.Tick += InitTimer_Tick;
            _initTimer.Start();
        }

        private async void InitTimer_Tick(object? sender, EventArgs e)
        {
            _initTimer.Stop();
            _initTimer.Dispose();

            try
            {
                // Now we're definitely on the UI thread
                var trayIcon = SystemIcons.Application;
                await _systemTrayManager.InitializeAsync(trayIcon, "TaskbarEqualizer - Professional Audio Visualizer");
                _logger.LogInformation("System tray initialized");

                // Make the tray icon visible
                await _systemTrayManager.ShowAsync();
                _logger.LogInformation("System tray icon shown");

                // Initialize and setup context menu
                _contextMenuManager = _serviceProvider.GetRequiredService<IContextMenuManager>();
                await _contextMenuManager.InitializeAsync();
                _logger.LogInformation("Context menu manager initialized");

                // Wire up context menu events
                _systemTrayManager.ContextMenuRequested += OnContextMenuRequested;
                _contextMenuManager.MenuItemClicked += OnMenuItemClicked;
                _logger.LogInformation("Context menu events wired up");

                // Remove this block - it conflicts with manual menu handling
                // if (_contextMenuManager.ContextMenuStrip != null)
                // {
                //     _systemTrayManager.SetContextMenuStrip(_contextMenuManager.ContextMenuStrip);
                //     _logger.LogInformation("Context menu strip assigned to system tray icon");
                // }

                // Start the orchestrator after system tray is ready
                await _orchestrator.StartAsync(default);
                _logger.LogInformation("Application orchestrator started");

                // Show startup notification (unless minimized)
                if (!_isMinimized)
                {
                    await _systemTrayManager.ShowBalloonTipAsync(
                        "TaskbarEqualizer Started",
                        "Professional audio visualizer is now running. Right-click the tray icon for options.",
                        BalloonTipIcon.Info);
                }

                _logger.LogInformation("TaskbarEqualizer initialization completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Failed to initialize TaskbarEqualizer components");

                // Exit the application if initialization fails
                ExitThread();
            }
        }

        /// <summary>
        /// Handles context menu requests from the system tray.
        /// </summary>
        private async void OnContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
        {
            try
            {
                _logger.LogInformation("📋 DIAGNOSTIC: ContextMenuRequested event received from {Sender} at {Location}", 
                    sender?.GetType().Name ?? "null", e.MenuLocation);
                
                if (_contextMenuManager != null)
                {
                    _logger.LogInformation("📋 DIAGNOSTIC: ContextMenuManager found, calling ShowMenuAsync at {Location}", e.MenuLocation);
                    await _contextMenuManager.ShowMenuAsync(e.MenuLocation);
                    _logger.LogInformation("📋 DIAGNOSTIC: ShowMenuAsync completed");
                }
                else
                {
                    _logger.LogError("📋 DIAGNOSTIC: ContextMenuManager is NULL - cannot show menu!");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "📋 DIAGNOSTIC: Error showing context menu at {Location}", e.MenuLocation);
            }
        }

        /// <summary>
        /// Handles menu item clicks.
        /// </summary>
        private void OnMenuItemClicked(object? sender, MenuItemClickedEventArgs e)
        {
            try
            {
                _logger.LogInformation("Menu item clicked: {ItemId} - {Text}", e.MenuItem.Id, e.MenuItem.Text);
                
                switch (e.MenuItem.Id.ToLowerInvariant())
                {
                    case "exit":
                        _logger.LogInformation("Exit requested from context menu");
                        Application.Exit();
                        break;
                    case "about":
                        MessageBox.Show(
                            "TaskbarEqualizer - Professional Audio Visualizer\nVersion 1.0\n\nReal-time audio visualization for Windows taskbar",
                            "About TaskbarEqualizer",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        break;
                    case "settings":
                        _logger.LogInformation("Settings requested from context menu");
                        MessageBox.Show(
                            "Settings Dialog\n\nThis feature will be implemented in a future version.\n\nFor now, you can configure the application through the system tray options.",
                            "Settings - Coming Soon",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        break;
                    default:
                        _logger.LogDebug("Unhandled menu item: {ItemId}", e.MenuItem.Id);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling menu item click: {ItemId}", e.MenuItem.Id);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Clean up event subscriptions
                if (_systemTrayManager != null)
                {
                    _systemTrayManager.ContextMenuRequested -= OnContextMenuRequested;
                }
                if (_contextMenuManager != null)
                {
                    _contextMenuManager.MenuItemClicked -= OnMenuItemClicked;
                }

                // Clean up resources
                _contextMenuManager?.Dispose();
                _systemTrayManager?.Dispose();
                _orchestrator?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}