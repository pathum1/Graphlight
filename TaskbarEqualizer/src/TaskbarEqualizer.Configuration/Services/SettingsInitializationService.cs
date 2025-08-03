using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Configuration.Interfaces;

namespace TaskbarEqualizer.Configuration.Services
{
    /// <summary>
    /// Background service that automatically initializes settings on application startup.
    /// </summary>
    public sealed class SettingsInitializationService : BackgroundService
    {
        private readonly ISettingsManager _settingsManager;
        private readonly ILogger<SettingsInitializationService> _logger;

        /// <summary>
        /// Initializes a new instance of the SettingsInitializationService.
        /// </summary>
        /// <param name="settingsManager">Settings manager to initialize.</param>
        /// <param name="logger">Logger for diagnostic information.</param>
        public SettingsInitializationService(
            ISettingsManager settingsManager,
            ILogger<SettingsInitializationService> logger)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Executes the background service to load settings.
        /// </summary>
        /// <param name="stoppingToken">Cancellation token for stopping the service.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting settings initialization");

            try
            {
                // Load settings if not already loaded
                if (!_settingsManager.IsLoaded)
                {
                    await _settingsManager.LoadAsync(stoppingToken);
                    _logger.LogInformation("Settings loaded successfully during initialization");
                }
                else
                {
                    _logger.LogDebug("Settings already loaded, skipping initialization");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Settings initialization cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize settings");
                // Don't throw - allow application to continue with default settings
            }

            _logger.LogDebug("Settings initialization service completed");
        }

        /// <summary>
        /// Called when the service is stopping.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the stop operation.</param>
        /// <returns>Task representing the asynchronous stop operation.</returns>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping settings initialization service");

            try
            {
                // Save any pending changes before stopping
                if (_settingsManager.IsDirty)
                {
                    await _settingsManager.SaveAsync(cancellationToken);
                    _logger.LogDebug("Saved pending settings changes during shutdown");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings during shutdown");
            }

            await base.StopAsync(cancellationToken);
            _logger.LogDebug("Settings initialization service stopped");
        }
    }
}