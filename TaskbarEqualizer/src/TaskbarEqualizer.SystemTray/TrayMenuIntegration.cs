using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Configuration;
using TaskbarEqualizer.Configuration.Interfaces;
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
        private readonly ISettingsManager _settingsManager;
        
        private bool _disposed = false;
        private bool _spectrumDataConnected = false;

        public TrayMenuIntegration(
            ILogger<TrayMenuIntegration> logger,
            ISystemTrayManager systemTrayManager,
            IContextMenuManager contextMenuManager,
            ITaskbarOverlayManager overlayManager,
            IServiceProvider serviceProvider,
            ISettingsManager settingsManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemTrayManager = systemTrayManager ?? throw new ArgumentNullException(nameof(systemTrayManager));
            _contextMenuManager = contextMenuManager ?? throw new ArgumentNullException(nameof(contextMenuManager));
            _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
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

                // Initialize overlay manager with default configuration if not already initialized
                // Check if overlay manager is not initialized (covers both NotInitialized and uninitialized states)
                if (_overlayManager.Configuration == null || !_overlayManager.IsActive)
                {
                    var overlayConfig = new Interfaces.OverlayConfiguration 
                    { 
                        Enabled = true, 
                        Width = 400, 
                        Height = 60, 
                        Opacity = 0.9f,
                        Position = Interfaces.OverlayPosition.Center,
                        UpdateFrequency = 60
                    };
                    await _overlayManager.InitializeAsync(overlayConfig);
                    _logger.LogInformation("TaskbarOverlayManager initialized with default configuration");
                }

                // Initialize context menu manager
                await _contextMenuManager.InitializeAsync();

                // Wire up event handlers
                _systemTrayManager.ContextMenuRequested += OnContextMenuRequested;
                _contextMenuManager.MenuItemClicked += OnMenuItemClicked;

                // Establish spectrum data connection early to ensure it's ready
                EnsureSpectrumDataConnection();

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
            _logger.LogInformation("=== DIAGNOSTIC: ShowAnalyzer called ===");
            
            // Check if frequency analyzer is running
            try
            {
                var freqAnalyzer = _serviceProvider.GetRequiredService<TaskbarEqualizer.Core.Interfaces.IFrequencyAnalyzer>();
                _logger.LogInformation("FrequencyAnalyzer.IsAnalyzing: {IsAnalyzing}", freqAnalyzer.IsAnalyzing);
                
                // Check if overlay manager is properly configured
                _logger.LogInformation("OverlayManager.IsActive: {IsActive}", _overlayManager.IsActive);
                _logger.LogInformation("OverlayManager.Configuration: {Configuration}", _overlayManager.Configuration);
                
                if (!_overlayManager.IsActive)
                {
                    _logger.LogInformation("Calling ShowAsync on overlay manager...");
                    await _overlayManager.ShowAsync();
                    _logger.LogInformation("ShowAsync completed. IsActive now: {IsActive}", _overlayManager.IsActive);
                    
                    // Ensure spectrum data connection is established only once
                    EnsureSpectrumDataConnection();
                    
                    // Force a test visualization update to make overlay visible
                    _logger.LogInformation("Sending test spectrum data to overlay...");
                    var testSpectrum = new TaskbarEqualizer.Core.Interfaces.SpectrumDataEventArgs(
                        new double[] { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8 }, 
                        8, 0.8, 4, DateTime.Now.Ticks, TimeSpan.Zero, 0.4);
                    _overlayManager.UpdateVisualization(testSpectrum);
                    _logger.LogInformation("Test spectrum data sent to overlay");
                    
                    // Show notification if enabled
                    if (_settingsManager.Settings.ShowNotifications)
                    {
                        await _systemTrayManager.ShowBalloonTipAsync(
                            "TaskbarEqualizer",
                            "Spectrum analyzer is now visible (with test data)",
                            BalloonTipIcon.Info,
                            3000
                        );
                    }
                }
                else
                {
                    _logger.LogInformation("Overlay is already active, no action needed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ShowAnalyzer diagnostic analysis");
                throw;
            }
        }

        /// <summary>
        /// Ensures that the spectrum data connection is established between the frequency analyzer and overlay.
        /// This prevents multiple subscriptions and ensures proper data flow.
        /// </summary>
        private void EnsureSpectrumDataConnection()
        {
            if (!_spectrumDataConnected)
            {
                try
                {
                    // Forward spectrum data to the overlay as per guide3.pdf
                    var freqAnalyzer = _serviceProvider.GetRequiredService<TaskbarEqualizer.Core.Interfaces.IFrequencyAnalyzer>();
                    freqAnalyzer.SpectrumDataAvailable += OnSpectrumDataAvailable;
                    _spectrumDataConnected = true;
                    _logger.LogDebug("Frequency analyzer connected to overlay for spectrum data forwarding");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to establish spectrum data connection");
                    throw;
                }
            }
        }

        /// <summary>
        /// Event handler for spectrum data updates from the frequency analyzer.
        /// </summary>
        private void OnSpectrumDataAvailable(object? sender, TaskbarEqualizer.Core.Interfaces.SpectrumDataEventArgs e)
        {
            try
            {
                if (_overlayManager.IsActive)
                {
                    _overlayManager.UpdateVisualization(e);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error updating overlay visualization with spectrum data");
            }
        }

        private async Task HideAnalyzer()
        {
            _logger.LogInformation("Hiding taskbar analyzer overlay");
            
            if (_overlayManager.IsActive)
            {
                await _overlayManager.HideAsync();
                
                // Show notification if enabled
                if (_settingsManager.Settings.ShowNotifications)
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
                using var settingsWindow = new SettingsWindow(settingsLogger, _overlayManager, _settingsManager.Settings);
                
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
                    if (_settingsManager.Settings.ShowNotifications)
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
                    
                    // Disconnect spectrum data if connected
                    if (_spectrumDataConnected)
                    {
                        try
                        {
                            var freqAnalyzer = _serviceProvider.GetService<TaskbarEqualizer.Core.Interfaces.IFrequencyAnalyzer>();
                            if (freqAnalyzer != null)
                            {
                                freqAnalyzer.SpectrumDataAvailable -= OnSpectrumDataAvailable;
                                _spectrumDataConnected = false;
                                _logger.LogDebug("Disconnected spectrum data event handler");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error disconnecting spectrum data event handler");
                        }
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