using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
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
        private object? _mainWindow; // Will be set from the main program
        private object? _spectrumWindow; // Reference to the spectrum analyzer window
        private bool _isSettingsDialogOpen; // Flag to prevent dialog re-opening

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

        /// <summary>
        /// Sets the main window reference for spectrum updates.
        /// </summary>
        /// <param name="mainWindow">The main spectrum analyzer window.</param>
        public void SetMainWindow(object mainWindow)
        {
            _mainWindow = mainWindow;
            
            // Initialize the spectrum window with current settings if available
            try
            {
                if (_settingsManager.IsLoaded && _mainWindow != null)
                {
                    var windowType = _mainWindow.GetType();
                    var initMethod = windowType.GetMethod("InitializeWithSettings");
                    
                    if (initMethod != null)
                    {
                        initMethod.Invoke(_mainWindow, new object[] { _settingsManager.Settings });
                        _logger.LogInformation("Main window initialized with current settings");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize main window with settings");
            }
        }

        /// <summary>
        /// Sets the spectrum window reference for visualization updates.
        /// </summary>
        /// <param name="spectrumWindow">The spectrum analyzer window instance.</param>
        public void SetSpectrumWindow(object spectrumWindow)
        {
            _spectrumWindow = spectrumWindow;
            _logger.LogInformation("Spectrum window reference set in orchestrator");
            
            // Initialize the spectrum window with current settings if available
            try
            {
                if (_settingsManager.IsLoaded && _spectrumWindow != null)
                {
                    _ = Task.Run(async () => await UpdateSpectrumWindowSettings(_settingsManager.Settings));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize spectrum window with settings");
            }
        }

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
                // Initialize all components in sequence (event handlers set up inside)
                await InitializeComponentsAsync(stoppingToken);

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

            // 2. Initialize context menu manager
            await _contextMenuManager.InitializeAsync(cancellationToken);
            _logger.LogDebug("Context menu manager initialized");

            // 3. Initialize frequency analyzer with settings-based configuration
            var settings = _settingsManager.Settings;
            await _frequencyAnalyzer.ConfigureAsync(
                fftSize: 2048,
                sampleRate: 44100,
                frequencyBands: settings.FrequencyBands,
                smoothingFactor: settings.SmoothingFactor,
                cancellationToken);
            _logger.LogDebug("Frequency analyzer configured");

            // 4. Initialize taskbar overlay
            var overlayConfig = new OverlayConfiguration
            {
                Enabled = true,
                Position = OverlayPosition.Center,
                Width = 500, // Reduced from 600 for better fit
                Height = 80, // Good height for visibility
                Opacity = 0.9f, // More opaque
                UpdateFrequency = 60
            };
            
            await _taskbarOverlayManager.InitializeAsync(overlayConfig, cancellationToken);
            _logger.LogDebug("Taskbar overlay manager initialized");

            // 5. Setup audio processing pipeline
            SetupAudioProcessingPipeline();

            // 6. Setup event handlers for UI interactions
            SetupEventHandlers();

            // 7. Enumerate and log available audio devices for debugging
            var availableDevices = _audioCaptureService.GetAvailableDevices();
            _logger.LogInformation("Found {DeviceCount} render devices for loopback capture", availableDevices.Length);
            
            // 8. Start audio capture and analysis with best device selection
            await _frequencyAnalyzer.StartAnalysisAsync(cancellationToken);
            await _audioCaptureService.StartBestLoopbackCaptureAsync(cancellationToken);
            _logger.LogDebug("Audio capture and analysis started");

            // 9. Show taskbar overlay
            await _taskbarOverlayManager.ShowAsync(cancellationToken);
            _logger.LogDebug("Taskbar overlay shown");

            // 10. Check auto-start status and sync with context menu
            var autoStartEnabled = await _autoStartManager.IsAutoStartEnabledAsync(cancellationToken);
            await _contextMenuManager.SetMenuItemCheckedAsync("autostart", autoStartEnabled, cancellationToken);
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

            // Connect frequency analyzer to taskbar overlay and spectrum window
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
        /// Handles spectrum data from the frequency analyzer and forwards to taskbar overlay and spectrum window.
        /// </summary>
        private void OnSpectrumDataAvailable(object? sender, SpectrumDataEventArgs e)
        {
            try
            {
                // Apply volume threshold gating
                var settings = _settingsManager.Settings;
                if (settings.VolumeThreshold > 0 && e.RmsLevel < settings.VolumeThreshold)
                {
                    // Volume is below threshold, send empty spectrum data to hide visualization
                    var emptySpectrum = new SpectrumDataEventArgs(
                        new double[e.Spectrum?.Length ?? 16], // Empty spectrum array
                        0, // No bands
                        0.0, // No peak
                        0, // No peak band index
                        e.TimestampTicks,
                        e.ProcessingLatency,
                        0.0 // No RMS
                    );
                    
                    _taskbarOverlayManager.UpdateVisualization(emptySpectrum);
                    
                    if (_spectrumWindow != null)
                    {
                        try
                        {
                            var spectrumWindowType = _spectrumWindow.GetType();
                            var updateMethod = spectrumWindowType.GetMethod("UpdateSpectrum");
                            updateMethod?.Invoke(_spectrumWindow, new object[] { emptySpectrum });
                        }
                        catch (Exception spectrumEx)
                        {
                            _logger.LogDebug(spectrumEx, "Error updating spectrum window with threshold data");
                        }
                    }
                    return;
                }

                // Spectrum data logging suppressed for performance - enable Trace level if needed for debugging
                
                // Update taskbar overlay
                _taskbarOverlayManager.UpdateVisualization(e);
                
                // Update spectrum window if available
                if (_spectrumWindow != null)
                {
                    try
                    {
                        var spectrumWindowType = _spectrumWindow.GetType();
                        var updateMethod = spectrumWindowType.GetMethod("UpdateSpectrum");
                        
                        if (updateMethod != null)
                        {
                            updateMethod.Invoke(_spectrumWindow, new object[] { e });
                        }
                    }
                    catch (Exception spectrumEx)
                    {
                        _logger.LogDebug(spectrumEx, "Error updating spectrum window visualization");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error updating visualizations");
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
                
                // Note: We removed the _isSettingsDialogOpen check here because:
                // 1. We DO want settings to be applied when the user clicks Apply
                // 2. The settings dialog uses cloned settings to prevent unwanted events during editing
                // 3. The only events that should reach here are from actual Apply operations
                var settings = _settingsManager.Settings;

                // Handle auto-start setting changes
                if (e.ChangedKeys.Contains("StartWithWindows"))
                {
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

                // Handle audio device settings changes
                var needsAudioCaptureUpdate = false;
                if (e.ChangedKeys.Contains("SelectedAudioDevice"))
                {
                    needsAudioCaptureUpdate = true;
                    _logger.LogDebug("Selected audio device changed to: {AudioDevice}", settings.SelectedAudioDevice);
                }

                // Handle spectrum analyzer settings changes
                var needsFrequencyAnalyzerUpdate = false;
                var needsSpectrumWindowUpdate = false;

                if (e.ChangedKeys.Contains("FrequencyBands"))
                {
                    needsFrequencyAnalyzerUpdate = true;
                    needsSpectrumWindowUpdate = true;
                    _logger.LogDebug("Frequency bands changed to: {FrequencyBands}", settings.FrequencyBands);
                }

                if (e.ChangedKeys.Contains("SmoothingFactor"))
                {
                    needsFrequencyAnalyzerUpdate = true;
                    needsSpectrumWindowUpdate = true;
                    _logger.LogDebug("Smoothing factor changed to: {SmoothingFactor}", settings.SmoothingFactor);
                }

                if (e.ChangedKeys.Contains("UpdateInterval"))
                {
                    needsSpectrumWindowUpdate = true;
                    _logger.LogDebug("Update interval changed to: {UpdateInterval}ms", settings.UpdateInterval);
                }

                if (e.ChangedKeys.Contains("GainFactor"))
                {
                    needsSpectrumWindowUpdate = true;
                    _logger.LogDebug("Gain factor changed to: {GainFactor}", settings.GainFactor);
                }

                if (e.ChangedKeys.Contains("VolumeThreshold"))
                {
                    needsSpectrumWindowUpdate = true;
                    _logger.LogDebug("Volume threshold changed to: {VolumeThreshold}", settings.VolumeThreshold);
                }

                // Additional visualization settings that require overlay updates
                var visualizationSettings = new[]
                {
                    "IconSize", "VisualizationStyle", "RenderQuality", "EnableAnimations", 
                    "EnableEffects", "UseCustomColors", "CustomPrimaryColor", "CustomSecondaryColor",
                    "EnableGradient", "GradientDirection", "Opacity", "AnimationSpeed",
                    "EnableBeatDetection", "EnableSpringPhysics", "SpringStiffness", 
                    "SpringDamping", "ChangeThreshold", "AdaptiveQuality", "MaxFrameRate"
                };

                if (visualizationSettings.Any(setting => e.ChangedKeys.Contains(setting)))
                {
                    needsSpectrumWindowUpdate = true;
                    var changedVizSettings = visualizationSettings.Where(setting => e.ChangedKeys.Contains(setting));
                    _logger.LogDebug("Visualization settings changed: {Settings}", string.Join(", ", changedVizSettings));
                }

                // Handle CustomSettings changes - these can contain color and visualization changes
                if (e.ChangedKeys.Contains("CustomSettings"))
                {
                    needsSpectrumWindowUpdate = true;
                    _logger.LogDebug("CustomSettings changed - updating spectrum window for potential color/style changes");
                }

                // Apply audio capture updates
                if (needsAudioCaptureUpdate)
                {
                    await UpdateAudioCaptureSettings(settings);
                }

                // Apply frequency analyzer updates
                if (needsFrequencyAnalyzerUpdate)
                {
                    await UpdateFrequencyAnalyzerAsync(settings);
                }

                // Apply spectrum window updates to TaskbarOverlayManager and Spectrum Window
                if (needsSpectrumWindowUpdate)
                {
                    await UpdateTaskbarOverlaySettingsAsync(settings);
                    await UpdateSpectrumWindowSettings(settings);
                }

                // Note: Settings are already saved by the caller (SettingsDialog or other source)
                // Removing duplicate SaveAsync call to prevent conflicts with bulk update mechanism
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

                // Sync context menu item state
                await _contextMenuManager.SetMenuItemCheckedAsync("autostart", e.IsEnabled);
                _logger.LogDebug("Updated context menu auto-start item to match status");
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
                _logger.LogInformation("Opening settings dialog");
                
                // We need to marshal to the UI thread for showing the dialog
                if (System.Windows.Forms.Application.MessageLoop)
                {
                    // Already on UI thread
                    ShowSettingsDialog();
                }
                else
                {
                    // Marshal to UI thread
                    await Task.Run(() =>
                    {
                        if (System.Windows.Forms.Control.CheckForIllegalCrossThreadCalls)
                        {
                            // Find any form to invoke on
                            var activeForm = System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().FirstOrDefault();
                            if (activeForm != null && activeForm.InvokeRequired)
                            {
                                activeForm.Invoke(new Action(ShowSettingsDialog));
                            }
                            else
                            {
                                ShowSettingsDialog();
                            }
                        }
                        else
                        {
                            ShowSettingsDialog();
                        }
                    });
                }
                
                _logger.LogDebug("Settings menu action completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle settings menu");
            }
        }

        /// <summary>
        /// Shows the settings dialog on the UI thread.
        /// </summary>
        private void ShowSettingsDialog()
        {
            try
            {
                // Fire a custom event that the application context can handle
                // This avoids complex dependency injection in the orchestrator
                OnSettingsDialogRequested();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to request settings dialog");
            }
        }

        /// <summary>
        /// Event fired when settings dialog should be shown.
        /// </summary>
        public event EventHandler? SettingsDialogRequested;

        /// <summary>
        /// Fires the settings dialog requested event.
        /// </summary>
        private void OnSettingsDialogRequested()
        {
            if (_isSettingsDialogOpen)
            {
                _logger.LogDebug("Settings dialog already open, ignoring duplicate request");
                return;
            }

            _isSettingsDialogOpen = true;
            SettingsDialogRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Marks the settings dialog as closed to allow future requests.
        /// </summary>
        public void OnSettingsDialogClosed()
        {
            _isSettingsDialogOpen = false;
            _logger.LogDebug("Settings dialog marked as closed");
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

                // Trigger application shutdown
                _logger.LogInformation("Initiating application shutdown...");
                
                // Use Application.Exit() to properly close the Windows Forms message loop
                Application.Exit();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during application exit");
            }
        }

        /// <summary>
        /// Updates the audio capture service with new settings.
        /// </summary>
        /// <param name="settings">The updated application settings.</param>
        private async Task UpdateAudioCaptureSettings(ApplicationSettings settings)
        {
            try
            {
                _logger.LogInformation("Updating audio capture settings");

                // Handle device switching if a specific device is selected
                if (!string.IsNullOrEmpty(settings.SelectedAudioDevice))
                {
                    var availableDevices = _audioCaptureService.GetAvailableDevices();
                    var targetDevice = availableDevices.FirstOrDefault(d => d.ID == settings.SelectedAudioDevice);
                    
                    if (targetDevice != null && targetDevice != _audioCaptureService.CurrentDevice)
                    {
                        _logger.LogInformation("Switching to selected audio device: {DeviceName}", targetDevice.FriendlyName);
                        await _audioCaptureService.SwitchDeviceAsync(targetDevice);
                    }
                }

                _logger.LogInformation("Audio capture settings updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update audio capture settings");
            }
        }

        /// <summary>
        /// Updates the frequency analyzer configuration with new settings.
        /// </summary>
        /// <param name="settings">The updated application settings.</param>
        private async Task UpdateFrequencyAnalyzerAsync(ApplicationSettings settings)
        {
            try
            {
                _logger.LogInformation("Updating frequency analyzer configuration");
                
                // Reconfigure the frequency analyzer with new settings
                await _frequencyAnalyzer.ConfigureAsync(
                    fftSize: 2048,
                    sampleRate: 44100,
                    frequencyBands: settings.FrequencyBands,
                    smoothingFactor: settings.SmoothingFactor,
                    cancellationToken: default);

                _logger.LogInformation("Frequency analyzer updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update frequency analyzer configuration");
            }
        }

        /// <summary>
        /// Updates the TaskbarOverlayManager with new visualization settings.
        /// </summary>
        /// <param name="settings">The updated application settings.</param>
        private async Task UpdateTaskbarOverlaySettingsAsync(ApplicationSettings settings)
        {
            try
            {
                _logger.LogInformation("Updating TaskbarOverlayManager with new settings");
                
                // Update the taskbar overlay manager with the new settings
                await _taskbarOverlayManager.UpdateSettingsAsync(settings);
                
                _logger.LogInformation("TaskbarOverlayManager updated successfully with new settings");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update TaskbarOverlayManager with new settings");
            }
        }

        /// <summary>
        /// Updates the spectrum window with new visualization settings.
        /// </summary>
        /// <param name="settings">The updated application settings.</param>
        private async Task UpdateSpectrumWindowSettings(ApplicationSettings settings)
        {
            try
            {
                if (_spectrumWindow == null)
                {
                    _logger.LogDebug("Spectrum window not available, skipping settings update");
                    return;
                }

                _logger.LogInformation("Updating spectrum window with new settings");
                
                // Call UpdateSettings method on the spectrum window using reflection
                var spectrumWindowType = _spectrumWindow.GetType();
                var updateMethod = spectrumWindowType.GetMethod("UpdateSettings");
                
                if (updateMethod != null)
                {
                    // Check if we need to invoke on UI thread
                    if (_spectrumWindow is System.Windows.Forms.Control control && control.InvokeRequired)
                    {
                        await Task.Run(() =>
                        {
                            control.Invoke(new Action(() => updateMethod.Invoke(_spectrumWindow, new object[] { settings })));
                        });
                    }
                    else
                    {
                        updateMethod.Invoke(_spectrumWindow, new object[] { settings });
                    }
                    
                    _logger.LogInformation("Spectrum window updated successfully with new settings");
                }
                else
                {
                    _logger.LogWarning("UpdateSettings method not found on spectrum window");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update spectrum window with new settings");
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