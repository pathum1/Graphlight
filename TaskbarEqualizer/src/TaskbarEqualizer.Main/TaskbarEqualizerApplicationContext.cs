using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
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
        private readonly bool _isMinimized;
        private Timer _initTimer;

        public TaskbarEqualizerApplicationContext(
            ISystemTrayManager systemTrayManager,
            ApplicationOrchestrator orchestrator,
            ILogger logger,
            bool isMinimized)
        {
            _systemTrayManager = systemTrayManager ?? throw new ArgumentNullException(nameof(systemTrayManager));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Clean up resources
                _systemTrayManager?.Dispose();
                _orchestrator?.Dispose();
            }
            
            base.Dispose(disposing);
        }
    }
}