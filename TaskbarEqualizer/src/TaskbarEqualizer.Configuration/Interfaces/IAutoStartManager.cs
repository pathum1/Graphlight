using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TaskbarEqualizer.Configuration.Interfaces
{
    /// <summary>
    /// Interface for managing Windows startup registration.
    /// Provides functionality to enable/disable application auto-start with Windows.
    /// </summary>
    public interface IAutoStartManager : IDisposable
    {
        /// <summary>
        /// Event fired when auto-start status changes.
        /// </summary>
        event EventHandler<AutoStartChangedEventArgs> AutoStartChanged;

        /// <summary>
        /// Gets a value indicating whether auto-start is currently enabled.
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Gets the current auto-start configuration.
        /// </summary>
        AutoStartConfiguration Configuration { get; }

        /// <summary>
        /// Checks if auto-start is currently enabled in the Windows registry.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task containing true if auto-start is enabled.</returns>
        Task<bool> IsAutoStartEnabledAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Enables auto-start with Windows.
        /// </summary>
        /// <param name="configuration">Auto-start configuration options.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous enable operation.</returns>
        Task EnableAutoStartAsync(AutoStartConfiguration? configuration = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Disables auto-start with Windows.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous disable operation.</returns>
        Task DisableAutoStartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the auto-start configuration without changing enabled state.
        /// </summary>
        /// <param name="configuration">New configuration to apply.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous update operation.</returns>
        Task UpdateConfigurationAsync(AutoStartConfiguration configuration, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current auto-start registry entry information.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task containing the registry entry details or null if not found.</returns>
        Task<AutoStartRegistryEntry?> GetRegistryEntryAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates that the current auto-start configuration is correct.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task containing the validation result.</returns>
        Task<AutoStartValidationResult> ValidateAutoStartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Repairs auto-start registration if it's corrupted or incorrect.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous repair operation.</returns>
        Task RepairAutoStartAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Configuration options for auto-start functionality.
    /// </summary>
    public class AutoStartConfiguration
    {
        /// <summary>
        /// Name of the application entry in the registry.
        /// </summary>
        public string ApplicationName { get; set; } = "TaskbarEqualizer";

        /// <summary>
        /// Path to the application executable.
        /// </summary>
        public string ExecutablePath { get; set; } = string.Empty;

        /// <summary>
        /// Command line arguments to pass when starting.
        /// </summary>
        public string Arguments { get; set; } = "--minimized";

        /// <summary>
        /// Whether to start the application minimized.
        /// </summary>
        public bool StartMinimized { get; set; } = true;

        /// <summary>
        /// Whether to use the current user registry (true) or local machine (false).
        /// </summary>
        public bool UseCurrentUserRegistry { get; set; } = true;

        /// <summary>
        /// Delay in seconds before starting the application (0 = no delay).
        /// </summary>
        public int StartDelaySeconds { get; set; } = 0;

        /// <summary>
        /// Description for the registry entry.
        /// </summary>
        public string Description { get; set; } = "TaskbarEqualizer Audio Visualizer";

        /// <summary>
        /// Creates a default auto-start configuration.
        /// </summary>
        /// <returns>New configuration with default values.</returns>
        public static AutoStartConfiguration CreateDefault()
        {
            return new AutoStartConfiguration
            {
                ExecutablePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty
            };
        }

        /// <summary>
        /// Creates a copy of this configuration.
        /// </summary>
        /// <returns>Deep copy of the configuration.</returns>
        public AutoStartConfiguration Clone()
        {
            return new AutoStartConfiguration
            {
                ApplicationName = ApplicationName,
                ExecutablePath = ExecutablePath,
                Arguments = Arguments,
                StartMinimized = StartMinimized,
                UseCurrentUserRegistry = UseCurrentUserRegistry,
                StartDelaySeconds = StartDelaySeconds,
                Description = Description
            };
        }

        /// <summary>
        /// Validates the configuration for correctness.
        /// </summary>
        /// <returns>True if the configuration is valid.</returns>
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(ApplicationName) &&
                   !string.IsNullOrWhiteSpace(ExecutablePath) &&
                   System.IO.File.Exists(ExecutablePath) &&
                   StartDelaySeconds >= 0 &&
                   StartDelaySeconds <= 300; // Max 5 minutes delay
        }
    }

    /// <summary>
    /// Information about an auto-start registry entry.
    /// </summary>
    public class AutoStartRegistryEntry
    {
        /// <summary>
        /// Name of the registry entry.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Command line value stored in the registry.
        /// </summary>
        public string CommandLine { get; set; } = string.Empty;

        /// <summary>
        /// Whether the entry exists in HKEY_CURRENT_USER or HKEY_LOCAL_MACHINE.
        /// </summary>
        public bool IsCurrentUser { get; set; }

        /// <summary>
        /// Registry key path where the entry is located.
        /// </summary>
        public string RegistryPath { get; set; } = string.Empty;

        /// <summary>
        /// When the registry entry was last modified.
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// Whether the referenced executable file exists.
        /// </summary>
        public bool ExecutableExists { get; set; }

        /// <summary>
        /// Path to the executable extracted from the command line.
        /// </summary>
        public string ExecutablePath { get; set; } = string.Empty;

        /// <summary>
        /// Arguments extracted from the command line.
        /// </summary>
        public string Arguments { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result of auto-start validation.
    /// </summary>
    public class AutoStartValidationResult
    {
        /// <summary>
        /// Whether the auto-start configuration is valid.
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// List of validation errors found.
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// List of validation warnings.
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>
        /// Whether the registry entry exists.
        /// </summary>
        public bool RegistryEntryExists { get; set; }

        /// <summary>
        /// Whether the executable path is correct.
        /// </summary>
        public bool ExecutablePathCorrect { get; set; }

        /// <summary>
        /// Whether the command line arguments are correct.
        /// </summary>
        public bool ArgumentsCorrect { get; set; }

        /// <summary>
        /// Current registry entry information if it exists.
        /// </summary>
        public AutoStartRegistryEntry? RegistryEntry { get; set; }
    }

    /// <summary>
    /// Event arguments for auto-start status changes.
    /// </summary>
    public class AutoStartChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Whether auto-start is now enabled.
        /// </summary>
        public bool IsEnabled { get; }

        /// <summary>
        /// The configuration that was applied.
        /// </summary>
        public AutoStartConfiguration Configuration { get; }

        /// <summary>
        /// Reason for the change.
        /// </summary>
        public AutoStartChangeReason Reason { get; }

        /// <summary>
        /// Whether the operation was successful.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Error message if the operation failed.
        /// </summary>
        public string? ErrorMessage { get; }

        public AutoStartChangedEventArgs(bool isEnabled, AutoStartConfiguration configuration, 
            AutoStartChangeReason reason, bool success = true, string? errorMessage = null)
        {
            IsEnabled = isEnabled;
            Configuration = configuration;
            Reason = reason;
            Success = success;
            ErrorMessage = errorMessage;
        }
    }

    /// <summary>
    /// Reasons for auto-start configuration changes.
    /// </summary>
    public enum AutoStartChangeReason
    {
        /// <summary>
        /// User manually enabled auto-start.
        /// </summary>
        UserEnabled,

        /// <summary>
        /// User manually disabled auto-start.
        /// </summary>
        UserDisabled,

        /// <summary>
        /// Configuration was updated.
        /// </summary>
        ConfigurationUpdated,

        /// <summary>
        /// Auto-start was repaired due to corruption.
        /// </summary>
        Repaired,

        /// <summary>
        /// System or external application modified the setting.
        /// </summary>
        SystemChanged,

        /// <summary>
        /// Application was reinstalled or updated.
        /// </summary>
        ApplicationUpdated
    }
}