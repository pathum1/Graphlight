using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Configuration.Interfaces;
using TaskbarEqualizer.SystemTray.Interfaces;
using TaskbarEqualizer.Core.Interfaces;

namespace TaskbarEqualizer.Configuration.Services
{
    /// <summary>
    /// Main application orchestrator that coordinates all Phase 3 user experience components.
    /// Manages the lifecycle and interactions between settings, context menu, and auto-start functionality.
    /// </summary>
    public sealed class ApplicationOrchestrator : BackgroundService
    {
        private readonly ISettingsManager _settingsManager;
        private readonly IContextMenuManager _contextMenuManager;
        private readonly IAutoStartManager _autoStartManager;
        private readonly ITaskbarOverlayManager _taskbarOverlayManager;
        private readonly IAudioCaptureService _audioCaptureService;
        private readonly IFrequencyAnalyzer _frequencyAnalyzer;
        private readonly ISystemTrayManager _systemTrayManager;
        private readonly ILogger<ApplicationOrchestrator> _logger;

        private bool _isInitialized;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the ApplicationOrchestrator.
        /// </summary>
        /// <param name="settingsManager">Settings manager for configuration persistence.</param>
        /// <param name="contextMenuManager">Context menu manager for user interface.</param>
        /// <param name="autoStartManager">Auto-start manager for Windows startup integration.</param>
        /// <param name="taskbarOverlayManager">Taskbar overlay manager for audio visualization.</param>
        /// <param name="audioCaptureService">Audio capture service for real-time audio data.</param>
        /// <param name="frequencyAnalyzer">Frequency analyzer for spectrum analysis.</param>
        /// <param name="systemTrayManager">System tray manager for tray icon and context menu.</param>
        /// <param name="logger">Logger for diagnostic information.</param>
        public ApplicationOrchestrator(
            ISettingsManager settingsManager,
            IContextMenuManager contextMenuManager,
            IAutoStartManager autoStartManager,
            ITaskbarOverlayManager taskbarOverlayManager,
            IAudioCaptureService audioCaptureService,
            IFrequencyAnalyzer frequencyAnalyzer,
            ISystemTrayManager systemTrayManager,
            ILogger<ApplicationOrchestrator> logger)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _contextMenuManager = contextMenuManager ?? throw new ArgumentNullException(nameof(contextMenuManager));
            _autoStartManager = autoStartManager ?? throw new ArgumentNullException(nameof(autoStartManager));
            _taskbarOverlayManager = taskbarOverlayManager ?? throw new ArgumentNullException(nameof(taskbarOverlayManager));
            _audioCaptureService = audioCaptureService ?? throw new ArgumentNullException(nameof(audioCaptureService));
            _frequencyAnalyzer = frequencyAnalyzer ?? throw new ArgumentNullException(nameof(frequencyAnalyzer));
            _systemTrayManager = systemTrayManager ?? throw new ArgumentNullException(nameof(systemTrayManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logger.LogDebug("ApplicationOrchestrator initialized");
        }

        #region Events

        /// <summary>
        /// Event fired when the application orchestrator is fully initialized.
        /// </summary>
        public event EventHandler? InitializationCompleted;

        /// <summary>
        /// Event fired when a critical error occurs that requires user attention.
        /// </summary>
        public event EventHandler<ApplicationErrorEventArgs>? CriticalError;

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether the orchestrator has been fully initialized.
        /// </summary>
        public bool IsInitialized => _isInitialized;

        #endregion

        #region BackgroundService Implementation

        /// <summary>
        /// Executes the orchestrator background service.
        /// </summary>
        /// <param name="stoppingToken">Cancellation token for stopping the service.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting application orchestrator");

            try
            {
                // Initialize all components in sequence
                await InitializeComponentsAsync(stoppingToken);

                // Setup event handlers and cross-component communication
                SetupEventHandlers();

                // Validate auto-start configuration
                await ValidateAutoStartConfigurationAsync(stoppingToken);

                _isInitialized = true;
                _logger.LogInformation("Application orchestrator initialization completed successfully");

                // Fire initialization completed event
                InitializationCompleted?.Invoke(this, EventArgs.Empty);

                // Keep the service running until cancellation is requested
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Application orchestrator startup cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Critical error during application orchestrator startup");
                
                // Fire critical error event
                var errorArgs = new ApplicationErrorEventArgs(ex, "Application orchestrator startup failed");
                CriticalError?.Invoke(this, errorArgs);
                
                throw;
            }
        }

        /// <summary>
        /// Called when the service is stopping.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the stop operation.</param>
        /// <returns>Task representing the asynchronous stop operation.</returns>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping application orchestrator");

            try
            {
                // Clean up event handlers
                CleanupEventHandlers();

                // Save any pending changes
                if (_settingsManager.IsDirty)
                {
                    await _settingsManager.SaveAsync(cancellationToken);
                    _logger.LogDebug("Saved pending settings changes during shutdown");
                }

                _isInitialized = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during application orchestrator shutdown");
            }

            await base.StopAsync(cancellationToken);
            _logger.LogInformation("Application orchestrator stopped");
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Initializes all Phase 3 components in the correct sequence.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        private async Task InitializeComponentsAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Initializing Phase 3 components with audio processing pipeline");

            // 1. Ensure settings are loaded first
            if (!_settingsManager.IsLoaded)
            {
                await _settingsManager.LoadAsync(cancellationToken);
                _logger.LogDebug("Settings loaded by orchestrator");
            }

            // 2. Initialize context menu (no async initialization needed)
            _logger.LogDebug("Context menu manager ready");

            // 3. Initialize frequency analyzer
            await _frequencyAnalyzer.ConfigureAsync(
                fftSize: 2048, // Increased to handle stereo audio (2 channels)
                sampleRate: 44100,
                frequencyBands: 32,
                smoothingFactor: 0.8,
                cancellationToken);
            _logger.LogDebug("Frequency analyzer configured");

            // 4. Initialize taskbar overlay
            var overlayConfig = new OverlayConfiguration
            {
                Enabled = true,
                Position = OverlayPosition.Center,
                Width = 300,
                Height = 40,
                Opacity = 0.8f,
                UpdateFrequency = 60
            };
            
            await _taskbarOverlayManager.InitializeAsync(overlayConfig, cancellationToken);
            _logger.LogDebug("Taskbar overlay manager initialized");

            // 5. Setup audio processing pipeline
            SetupAudioProcessingPipeline();

            // 6. Start audio capture and analysis
            await _frequencyAnalyzer.StartAnalysisAsync(cancellationToken);
            await _audioCaptureService.StartCaptureAsync(cancellationToken);
            _logger.LogDebug("Audio capture and analysis started");

            // 7. Show taskbar overlay
            await _taskbarOverlayManager.ShowAsync(cancellationToken);
            _logger.LogDebug("Taskbar overlay shown");

            // 8. Check auto-start status
            var autoStartEnabled = await _autoStartManager.IsAutoStartEnabledAsync(cancellationToken);
            _logger.LogDebug("Auto-start status checked: {Enabled}", autoStartEnabled);

            _logger.LogDebug("All Phase 3 components initialized successfully");
        }

        /// <summary>
        /// Sets up the audio processing pipeline by connecting services.
        /// </summary>
        private void SetupAudioProcessingPipeline()
        {
            _logger.LogDebug("Setting up audio processing pipeline");

            // Connect audio capture to frequency analyzer
            _audioCaptureService.AudioDataAvailable += OnAudioDataAvailable;

            // Connect frequency analyzer to taskbar overlay
            _frequencyAnalyzer.SpectrumDataAvailable += OnSpectrumDataAvailable;

            _logger.LogDebug("Audio processing pipeline configured");
        }

        /// <summary>
        /// Handles audio data from the capture service and forwards to frequency analyzer.
        /// </summary>
        private async void OnAudioDataAvailable(object? sender, AudioDataEventArgs e)
        {
            try
            {
                await _frequencyAnalyzer.ProcessAudioSamplesAsync(e.Samples, e.SampleCount, e.TimestampTicks);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing audio data");
            }
        }

        /// <summary>
        /// Handles spectrum data from the frequency analyzer and forwards to taskbar overlay.
        /// </summary>
        private void OnSpectrumDataAvailable(object? sender, SpectrumDataEventArgs e)
        {
            try
            {
                _taskbarOverlayManager.UpdateVisualization(e);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error updating taskbar visualization");
            }
        }

        /// <summary>
        /// Handles context menu requests from the system tray.
        /// </summary>
        private async void OnContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
        {
            try
            {
                _logger.LogDebug("Context menu requested at location {Location}", e.MenuLocation);
                await _contextMenuManager.ShowMenuAsync(e.MenuLocation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing context menu");
            }
        }

        /// <summary>
        /// Sets up event handlers for cross-component communication.
        /// </summary>
        private void SetupEventHandlers()
        {
            _logger.LogDebug("Setting up cross-component event handlers");

            // Handle context menu item clicks
            _contextMenuManager.MenuItemClicked += OnContextMenuItemClicked;

            // Handle context menu requests from system tray
            _systemTrayManager.ContextMenuRequested += OnContextMenuRequested;

            // Handle settings changes
            _settingsManager.SettingsChanged += OnSettingsChanged;

            // Handle auto-start status changes
            _autoStartManager.AutoStartChanged += OnAutoStartChanged;

            _logger.LogDebug("Event handlers configured successfully");
        }

        /// <summary>
        /// Cleans up event handlers during shutdown.
        /// </summary>
        private void CleanupEventHandlers()
        {
            _logger.LogDebug("Cleaning up event handlers");

            _contextMenuManager.MenuItemClicked -= OnContextMenuItemClicked;
            _systemTrayManager.ContextMenuRequested -= OnContextMenuRequested;
            _settingsManager.SettingsChanged -= OnSettingsChanged;
            _autoStartManager.AutoStartChanged -= OnAutoStartChanged;

            _logger.LogDebug("Event handlers cleaned up");
        }

        /// <summary>
        /// Validates and synchronizes auto-start configuration with current settings.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        private async Task ValidateAutoStartConfigurationAsync(CancellationToken cancellationToken)
        {
            try
            {
                var settings = _settingsManager.Settings;
                var currentlyEnabled = await _autoStartManager.IsAutoStartEnabledAsync(cancellationToken);

                // Synchronize auto-start state with settings
                if (settings.StartWithWindows && !currentlyEnabled)
                {
                    _logger.LogInformation("Enabling auto-start to match settings");
                    await _autoStartManager.EnableAutoStartAsync(null, cancellationToken);
                }
                else if (!settings.StartWithWindows && currentlyEnabled)
                {
                    _logger.LogInformation("Disabling auto-start to match settings");
                    await _autoStartManager.DisableAutoStartAsync(cancellationToken);
                }

                // Validate current configuration
                var validationResult = await _autoStartManager.ValidateAutoStartAsync(cancellationToken);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("Auto-start configuration validation failed: {Errors}", 
                        string.Join(", ", validationResult.Errors));
                    
                    // Attempt repair
                    await _autoStartManager.RepairAutoStartAsync(cancellationToken);
                    _logger.LogInformation("Auto-start configuration repaired");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate auto-start configuration");
            }
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handles context menu item click events.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event arguments.</param>
        private async void OnContextMenuItemClicked(object? sender, TaskbarEqualizer.SystemTray.Interfaces.MenuItemClickedEventArgs e)
        {
            try
            {
                _logger.LogDebug("Context menu item clicked: {ItemId}", e.MenuItem.Id);

                switch (e.MenuItem.Id.ToLowerInvariant())
                {
                    case "autostart":
                        await HandleAutoStartToggleAsync();
                        break;

                    case "settings":
                        await HandleSettingsMenuAsync();
                        break;

                    case "exit":
                        await HandleExitMenuAsync();
                        break;

                    default:
                        _logger.LogDebug("Unhandled menu item: {ItemId}", e.MenuItem.Id);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling context menu item click: {ItemId}", e.MenuItem.Id);
            }
        }

        /// <summary>
        /// Handles settings change events.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event arguments.</param>
        private async void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
        {
            try
            {
                _logger.LogDebug("Settings changed: {ChangedKeys}", string.Join(", ", e.ChangedKeys));

                // Handle auto-start setting changes
                if (e.ChangedKeys.Contains("StartWithWindows"))
                {
                    var settings = _settingsManager.Settings;
                    if (settings.StartWithWindows)
                    {
                        await _autoStartManager.EnableAutoStartAsync();
                        _logger.LogInformation("Auto-start enabled via settings");
                    }
                    else
                    {
                        await _autoStartManager.DisableAutoStartAsync();
                        _logger.LogInformation("Auto-start disabled via settings");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling settings change");
            }
        }

        /// <summary>
        /// Handles auto-start status change events.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event arguments.</param>
        private async void OnAutoStartChanged(object? sender, AutoStartChangedEventArgs e)
        {
            try
            {
                _logger.LogDebug("Auto-start status changed: Enabled={Enabled}, Reason={Reason}", 
                    e.IsEnabled, e.Reason);

                // Update settings to reflect auto-start status changes
                var settings = _settingsManager.Settings;
                if (settings.StartWithWindows != e.IsEnabled)
                {
                    await _settingsManager.SetSetting("StartWithWindows", e.IsEnabled);
                    _logger.LogDebug("Updated StartWithWindows setting to match auto-start status");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling auto-start status change");
            }
        }

        /// <summary>
        /// Handles auto-start toggle menu action.
        /// </summary>
        /// <returns>Task representing the asynchronous operation.</returns>
        private async Task HandleAutoStartToggleAsync()
        {
            try
            {
                var currentlyEnabled = await _autoStartManager.IsAutoStartEnabledAsync();
                
                if (currentlyEnabled)
                {
                    await _autoStartManager.DisableAutoStartAsync();
                    _logger.LogInformation("Auto-start disabled by user");
                }
                else
                {
                    await _autoStartManager.EnableAutoStartAsync();
                    _logger.LogInformation("Auto-start enabled by user");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle auto-start");
            }
        }

        /// <summary>
        /// Handles settings menu action.
        /// </summary>
        /// <returns>Task representing the asynchronous operation.</returns>
        private async Task HandleSettingsMenuAsync()
        {
            try
            {
                _logger.LogInformation("Opening settings interface");
                
                // For now, just log - in Phase 4 this would open a settings window
                await Task.CompletedTask;
                
                _logger.LogDebug("Settings menu action completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle settings menu");
            }
        }

        /// <summary>
        /// Handles application exit menu action.
        /// </summary>
        /// <returns>Task representing the asynchronous operation.</returns>
        private async Task HandleExitMenuAsync()
        {
            try
            {
                _logger.LogInformation("Application exit requested by user");
                
                // Save any pending changes before exit
                if (_settingsManager.IsDirty)
                {
                    await _settingsManager.SaveAsync();
                    _logger.LogDebug("Saved pending settings before exit");
                }

                // In Phase 4, this would trigger application shutdown
                // For now, just log the request
                _logger.LogInformation("Exit request processed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during application exit");
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the application orchestrator and releases resources.
        /// </summary>
        public override void Dispose()
        {
            if (!_disposed)
            {
                CleanupEventHandlers();
                _disposed = true;
                _logger.LogDebug("ApplicationOrchestrator disposed");
            }

            base.Dispose();
        }

        #endregion
    }

    /// <summary>
    /// Event arguments for critical application errors.
    /// </summary>
    public class ApplicationErrorEventArgs : EventArgs
    {
        /// <summary>
        /// The exception that occurred.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// Description of the error context.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Whether the error is recoverable.
        /// </summary>
        public bool IsRecoverable { get; }

        /// <summary>
        /// Initializes a new instance of the ApplicationErrorEventArgs.
        /// </summary>
        /// <param name="exception">The exception that occurred.</param>
        /// <param name="description">Description of the error context.</param>
        /// <param name="isRecoverable">Whether the error is recoverable.</param>
        public ApplicationErrorEventArgs(Exception exception, string description, bool isRecoverable = false)
        {
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            IsRecoverable = isRecoverable;
        }
    }
}