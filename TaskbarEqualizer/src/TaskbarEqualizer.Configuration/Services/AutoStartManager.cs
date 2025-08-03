using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using TaskbarEqualizer.Configuration.Interfaces;

namespace TaskbarEqualizer.Configuration.Services
{
    /// <summary>
    /// Windows registry-based auto-start manager.
    /// Manages application startup registration with Windows.
    /// </summary>
    public sealed class AutoStartManager : IAutoStartManager
    {
        private readonly ILogger<AutoStartManager> _logger;
        private readonly object _registryLock = new();
        
        private AutoStartConfiguration _configuration;
        private bool _isEnabled;
        private bool _disposed;

        // Registry paths for auto-start
        private const string CurrentUserRunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string LocalMachineRunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>
        /// Initializes a new instance of the AutoStartManager.
        /// </summary>
        /// <param name="logger">Logger for diagnostic information.</param>
        public AutoStartManager(ILogger<AutoStartManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = AutoStartConfiguration.CreateDefault();
            
            _logger.LogDebug("AutoStartManager initialized");
        }

        #region Events

        /// <inheritdoc />
        public event EventHandler<AutoStartChangedEventArgs>? AutoStartChanged;

        #endregion

        #region Properties

        /// <inheritdoc />
        public bool IsEnabled => _isEnabled;

        /// <inheritdoc />
        public AutoStartConfiguration Configuration => _configuration;

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public async Task<bool> IsAutoStartEnabledAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AutoStartManager));

            try
            {
                var result = await Task.Run(() =>
                {
                    lock (_registryLock)
                    {
                        var entry = GetRegistryEntryInternal();
                        var enabled = entry != null;
                        
                        if (_isEnabled != enabled)
                        {
                            _isEnabled = enabled;
                            _logger.LogDebug("Auto-start status updated: {Enabled}", enabled);
                        }

                        return enabled;
                    }
                }, cancellationToken);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check auto-start status");
                return false;
            }
        }

        /// <inheritdoc />
        public async Task EnableAutoStartAsync(AutoStartConfiguration? configuration = null, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AutoStartManager));

            var config = configuration ?? _configuration;
            
            if (!config.IsValid())
                throw new ArgumentException("Invalid auto-start configuration", nameof(configuration));

            _logger.LogInformation("Enabling auto-start for {ApplicationName}", config.ApplicationName);

            try
            {
                await Task.Run(() =>
                {
                    lock (_registryLock)
                    {
                        var registryKey = config.UseCurrentUserRegistry 
                            ? Registry.CurrentUser.OpenSubKey(CurrentUserRunKeyPath, true)
                            : Registry.LocalMachine.OpenSubKey(LocalMachineRunKeyPath, true);

                        if (registryKey == null)
                        {
                            throw new InvalidOperationException("Unable to open registry key for auto-start");
                        }

                        using (registryKey)
                        {
                            var commandLine = BuildCommandLine(config);
                            registryKey.SetValue(config.ApplicationName, commandLine, RegistryValueKind.String);
                            
                            _logger.LogDebug("Registry entry created: {Name} = {CommandLine}", 
                                config.ApplicationName, commandLine);
                        }

                        _configuration = config.Clone();
                        _isEnabled = true;
                    }
                }, cancellationToken);

                // Fire event
                var eventArgs = new AutoStartChangedEventArgs(true, config, AutoStartChangeReason.UserEnabled, true);
                AutoStartChanged?.Invoke(this, eventArgs);

                _logger.LogInformation("Auto-start enabled successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enable auto-start");
                
                // Fire event with error
                var eventArgs = new AutoStartChangedEventArgs(false, config, AutoStartChangeReason.UserEnabled, false, ex.Message);
                AutoStartChanged?.Invoke(this, eventArgs);
                
                throw;
            }
        }

        /// <inheritdoc />
        public async Task DisableAutoStartAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AutoStartManager));

            _logger.LogInformation("Disabling auto-start for {ApplicationName}", _configuration.ApplicationName);

            try
            {
                await Task.Run(() =>
                {
                    lock (_registryLock)
                    {
                        // Try both registry locations
                        var locations = new[]
                        {
                            (Registry.CurrentUser, CurrentUserRunKeyPath, true),
                            (Registry.LocalMachine, LocalMachineRunKeyPath, false)
                        };

                        bool removed = false;

                        foreach (var (registryHive, keyPath, isCurrentUser) in locations)
                        {
                            try
                            {
                                using var registryKey = registryHive.OpenSubKey(keyPath, true);
                                if (registryKey != null)
                                {
                                    var existingValue = registryKey.GetValue(_configuration.ApplicationName);
                                    if (existingValue != null)
                                    {
                                        registryKey.DeleteValue(_configuration.ApplicationName, false);
                                        removed = true;
                                        
                                        _logger.LogDebug("Removed registry entry from {Location}: {Name}", 
                                            isCurrentUser ? "HKCU" : "HKLM", _configuration.ApplicationName);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to remove auto-start entry from {Location}", 
                                    isCurrentUser ? "HKCU" : "HKLM");
                            }
                        }

                        if (removed)
                        {
                            _isEnabled = false;
                        }
                    }
                }, cancellationToken);

                // Fire event
                var eventArgs = new AutoStartChangedEventArgs(false, _configuration, AutoStartChangeReason.UserDisabled, true);
                AutoStartChanged?.Invoke(this, eventArgs);

                _logger.LogInformation("Auto-start disabled successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to disable auto-start");
                
                // Fire event with error
                var eventArgs = new AutoStartChangedEventArgs(true, _configuration, AutoStartChangeReason.UserDisabled, false, ex.Message);
                AutoStartChanged?.Invoke(this, eventArgs);
                
                throw;
            }
        }

        /// <inheritdoc />
        public async Task UpdateConfigurationAsync(AutoStartConfiguration configuration, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AutoStartManager));

            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            if (!configuration.IsValid())
                throw new ArgumentException("Invalid auto-start configuration", nameof(configuration));

            _logger.LogDebug("Updating auto-start configuration");

            try
            {
                var wasEnabled = await IsAutoStartEnabledAsync(cancellationToken);

                if (wasEnabled)
                {
                    // Disable first, then re-enable with new configuration
                    await DisableAutoStartAsync(cancellationToken);
                    await EnableAutoStartAsync(configuration, cancellationToken);
                }
                else
                {
                    // Just update the configuration
                    _configuration = configuration.Clone();
                }

                // Fire event
                var eventArgs = new AutoStartChangedEventArgs(_isEnabled, configuration, AutoStartChangeReason.ConfigurationUpdated, true);
                AutoStartChanged?.Invoke(this, eventArgs);

                _logger.LogDebug("Auto-start configuration updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update auto-start configuration");
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<AutoStartRegistryEntry?> GetRegistryEntryAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AutoStartManager));

            try
            {
                var result = await Task.Run(() =>
                {
                    lock (_registryLock)
                    {
                        return GetRegistryEntryInternal();
                    }
                }, cancellationToken);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get registry entry");
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<AutoStartValidationResult> ValidateAutoStartAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AutoStartManager));

            var result = new AutoStartValidationResult();

            try
            {
                var registryEntry = await GetRegistryEntryAsync(cancellationToken);
                
                result.RegistryEntryExists = registryEntry != null;
                result.RegistryEntry = registryEntry;

                if (registryEntry != null)
                {
                    // Validate executable path
                    result.ExecutablePathCorrect = File.Exists(registryEntry.ExecutablePath);
                    if (!result.ExecutablePathCorrect)
                    {
                        result.Errors.Add($"Executable not found: {registryEntry.ExecutablePath}");
                    }

                    // Validate against current configuration
                    var expectedCommandLine = BuildCommandLine(_configuration);
                    result.ArgumentsCorrect = string.Equals(registryEntry.CommandLine, expectedCommandLine, StringComparison.OrdinalIgnoreCase);
                    
                    if (!result.ArgumentsCorrect)
                    {
                        result.Warnings.Add("Registry command line doesn't match current configuration");
                    }

                    // Check if executable is current process
                    var currentProcessPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(currentProcessPath) && 
                        !string.Equals(registryEntry.ExecutablePath, currentProcessPath, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Warnings.Add("Registry points to different executable than current process");
                    }
                }
                else if (_isEnabled)
                {
                    result.Errors.Add("Auto-start is marked as enabled but no registry entry found");
                }

                result.IsValid = result.Errors.Count == 0;

                _logger.LogDebug("Auto-start validation completed: Valid={IsValid}, Errors={ErrorCount}, Warnings={WarningCount}",
                    result.IsValid, result.Errors.Count, result.Warnings.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate auto-start");
                result.IsValid = false;
                result.Errors.Add($"Validation error: {ex.Message}");
            }

            return result;
        }

        /// <inheritdoc />
        public async Task RepairAutoStartAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AutoStartManager));

            _logger.LogInformation("Repairing auto-start configuration");

            try
            {
                var validationResult = await ValidateAutoStartAsync(cancellationToken);

                if (validationResult.IsValid)
                {
                    _logger.LogDebug("Auto-start configuration is already valid, no repair needed");
                    return;
                }

                // If we're supposed to be enabled but registry entry is missing or incorrect
                if (_isEnabled)
                {
                    _logger.LogInformation("Re-enabling auto-start to repair configuration");
                    await EnableAutoStartAsync(_configuration, cancellationToken);
                }
                // If registry entry exists but we're not tracking it as enabled
                else if (validationResult.RegistryEntryExists)
                {
                    _logger.LogInformation("Removing orphaned registry entry");
                    await DisableAutoStartAsync(cancellationToken);
                }

                // Fire event
                var eventArgs = new AutoStartChangedEventArgs(_isEnabled, _configuration, AutoStartChangeReason.Repaired, true);
                AutoStartChanged?.Invoke(this, eventArgs);

                _logger.LogInformation("Auto-start repair completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to repair auto-start");
                throw;
            }
        }

        #endregion

        #region Private Methods

        private AutoStartRegistryEntry? GetRegistryEntryInternal()
        {
            // Check both registry locations
            var locations = new[]
            {
                (Registry.CurrentUser, CurrentUserRunKeyPath, true),
                (Registry.LocalMachine, LocalMachineRunKeyPath, false)
            };

            foreach (var (registryHive, keyPath, isCurrentUser) in locations)
            {
                try
                {
                    using var registryKey = registryHive.OpenSubKey(keyPath, false);
                    if (registryKey != null)
                    {
                        var value = registryKey.GetValue(_configuration.ApplicationName) as string;
                        if (!string.IsNullOrEmpty(value))
                        {
                            var (executablePath, arguments) = ParseCommandLine(value);
                            
                            return new AutoStartRegistryEntry
                            {
                                Name = _configuration.ApplicationName,
                                CommandLine = value,
                                IsCurrentUser = isCurrentUser,
                                RegistryPath = $"{(isCurrentUser ? "HKCU" : "HKLM")}\\{keyPath}",
                                ExecutablePath = executablePath,
                                Arguments = arguments,
                                ExecutableExists = File.Exists(executablePath),
                                LastModified = DateTime.Now // Registry doesn't provide last modified time easily
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read registry key: {KeyPath}", keyPath);
                }
            }

            return null;
        }

        private string BuildCommandLine(AutoStartConfiguration config)
        {
            var commandLine = $"\"{config.ExecutablePath}\"";
            
            if (!string.IsNullOrWhiteSpace(config.Arguments))
            {
                commandLine += $" {config.Arguments}";
            }

            if (config.StartDelaySeconds > 0)
            {
                commandLine += $" --delay {config.StartDelaySeconds}";
            }

            return commandLine;
        }

        private (string executablePath, string arguments) ParseCommandLine(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
                return (string.Empty, string.Empty);

            // Use regex to parse quoted executable path and arguments
            var match = Regex.Match(commandLine, @"^""([^""]+)""\s*(.*)$");
            if (match.Success)
            {
                return (match.Groups[1].Value, match.Groups[2].Value.Trim());
            }

            // Fallback: split by first space
            var parts = commandLine.Split(' ', 2);
            if (parts.Length == 2)
            {
                return (parts[0].Trim('"'), parts[1]);
            }

            return (commandLine.Trim('"'), string.Empty);
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the auto-start manager and releases resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _logger.LogDebug("AutoStartManager disposed");
            }
        }

        #endregion
    }
}