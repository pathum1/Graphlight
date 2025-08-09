using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Configuration;
using TaskbarEqualizer.SystemTray.ContextMenu;
using TaskbarEqualizer.SystemTray.Forms;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.SystemTray
{
    /// <summary>
    /// Integrates the system tray manager with the context menu manager and settings dialog.
    /// Provides a complete system tray experience with right-click context menu functionality.
    /// </summary>
    public class TrayMenuIntegration : IDisposable
    {
        private readonly ILogger<TrayMenuIntegration> _logger;
        private readonly ISystemTrayManager _systemTrayManager;
        private readonly IContextMenuManager _contextMenuManager;
        private readonly ITaskbarOverlayManager _overlayManager;
        private readonly IServiceProvider _serviceProvider;
        private readonly ApplicationSettings _applicationSettings;
        
        private bool _disposed = false;

        public TrayMenuIntegration(
            ILogger<TrayMenuIntegration> logger,
            ISystemTrayManager systemTrayManager,
            IContextMenuManager contextMenuManager,
            ITaskbarOverlayManager overlayManager,
            IServiceProvider serviceProvider,
            ApplicationSettings applicationSettings)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemTrayManager = systemTrayManager ?? throw new ArgumentNullException(nameof(systemTrayManager));
            _contextMenuManager = contextMenuManager ?? throw new ArgumentNullException(nameof(contextMenuManager));
            _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _applicationSettings = applicationSettings ?? throw new ArgumentNullException(nameof(applicationSettings));
        }

        /// <summary>
        /// Initializes the tray menu integration by wiring up event handlers.
        /// </summary>
        /// <returns>Task representing the initialization operation.</returns>
        public async Task InitializeAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TrayMenuIntegration));

            try
            {
                _logger.LogInformation("Initializing tray menu integration");

                // Initialize context menu manager
                await _contextMenuManager.InitializeAsync();

                // Wire up event handlers
                _systemTrayManager.ContextMenuRequested += OnContextMenuRequested;
                _contextMenuManager.MenuItemClicked += OnMenuItemClicked;

                _logger.LogInformation("Tray menu integration initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize tray menu integration");
                throw;
            }
        }

        private async void OnContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
        {
            try
            {
                _logger.LogDebug("Context menu requested at position: {Position}", e.MenuLocation);
                
                // Update menu item states based on current overlay state
                await UpdateMenuItemStates();
                
                // Show the context menu at the requested location
                await _contextMenuManager.ShowMenuAsync(e.MenuLocation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show context menu");
            }
        }

        private async void OnMenuItemClicked(object? sender, MenuItemClickedEventArgs e)
        {
            try
            {
                _logger.LogDebug("Menu item clicked: {Id}", e.MenuItem.Id);

                switch (e.MenuItem.Id)
                {
                    case "show_analyzer":
                        await ShowAnalyzer();
                        break;
                        
                    case "hide_analyzer":
                        await HideAnalyzer();
                        break;
                        
                    case "settings":
                        await ShowSettings();
                        break;
                        
                    default:
                        // Other menu items are handled by the ContextMenuManager itself
                        break;
                }
                
                // Update menu states after action
                await UpdateMenuItemStates();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle menu item click for {Id}", e.MenuItem.Id);
                
                // Show user-friendly error message
                MessageBox.Show(
                    $"An error occurred: {ex.Message}",
                    "TaskbarEqualizer Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

        private async Task ShowAnalyzer()
        {
            _logger.LogInformation("Showing taskbar analyzer overlay");
            
            if (!_overlayManager.IsActive)
            {
                await _overlayManager.ShowAsync();
                
                // Show notification if enabled
                if (_applicationSettings.ShowNotifications)
                {
                    await _systemTrayManager.ShowBalloonTipAsync(
                        "TaskbarEqualizer",
                        "Spectrum analyzer is now visible",
                        BalloonTipIcon.Info,
                        3000
                    );
                }
            }
        }

        private async Task HideAnalyzer()
        {
            _logger.LogInformation("Hiding taskbar analyzer overlay");
            
            if (_overlayManager.IsActive)
            {
                await _overlayManager.HideAsync();
                
                // Show notification if enabled
                if (_applicationSettings.ShowNotifications)
                {
                    await _systemTrayManager.ShowBalloonTipAsync(
                        "TaskbarEqualizer", 
                        "Spectrum analyzer is now hidden",
                        BalloonTipIcon.Info,
                        3000
                    );
                }
            }
        }

        private async Task ShowSettings()
        {
            _logger.LogInformation("Opening settings window");
            
            try
            {
                // Get required services for the settings window
                var settingsLogger = _serviceProvider.GetRequiredService<ILogger<SettingsWindow>>();
                
                // Create and show settings window
                using var settingsWindow = new SettingsWindow(settingsLogger, _overlayManager, _applicationSettings);
                
                var result = settingsWindow.ShowDialog();
                
                if (result == DialogResult.OK)
                {
                    _logger.LogInformation("Settings were applied successfully");
                    
                    // Save settings if there's a settings manager available
                    var settingsManager = _serviceProvider.GetService<TaskbarEqualizer.Configuration.Interfaces.ISettingsManager>();
                    if (settingsManager != null)
                    {
                        try
                        {
                            await settingsManager.SaveAsync();
                            _logger.LogDebug("Settings saved to persistent storage");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to save settings to persistent storage");
                        }
                    }
                    
                    // Show success notification if enabled
                    if (_applicationSettings.ShowNotifications)
                    {
                        await _systemTrayManager.ShowBalloonTipAsync(
                            "TaskbarEqualizer",
                            "Settings have been applied",
                            BalloonTipIcon.Info,
                            2000
                        );
                    }
                }
                else
                {
                    _logger.LogDebug("Settings dialog was cancelled");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show settings window");
                
                MessageBox.Show(
                    "Failed to open settings window. Please try again.",
                    "TaskbarEqualizer Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private async Task UpdateMenuItemStates()
        {
            try
            {
                // Update Show/Hide analyzer menu items based on current state
                bool isAnalyzerActive = _overlayManager.IsActive;
                
                await _contextMenuManager.SetMenuItemEnabledAsync("show_analyzer", !isAnalyzerActive);
                await _contextMenuManager.SetMenuItemEnabledAsync("hide_analyzer", isAnalyzerActive);
                
                _logger.LogDebug("Updated menu item states - Analyzer active: {IsActive}", isAnalyzerActive);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update menu item states");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                
                try
                {
                    // Unsubscribe from events
                    if (_systemTrayManager != null)
                    {
                        _systemTrayManager.ContextMenuRequested -= OnContextMenuRequested;
                    }
                    
                    if (_contextMenuManager != null)
                    {
                        _contextMenuManager.MenuItemClicked -= OnMenuItemClicked;
                    }
                    
                    _logger.LogDebug("TrayMenuIntegration disposed");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error during TrayMenuIntegration disposal");
                }
            }
        }
    }

}