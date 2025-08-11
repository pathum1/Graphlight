using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace TaskbarEqualizer.Configuration.Interfaces
{
    /// <summary>
    /// Interface for managing application settings with persistence.
    /// Provides type-safe configuration management with automatic saving.
    /// </summary>
    public interface ISettingsManager : INotifyPropertyChanged, IDisposable
    {
        /// <summary>
        /// Event fired when settings are loaded from storage.
        /// </summary>
        event EventHandler<SettingsLoadedEventArgs> SettingsLoaded;

        /// <summary>
        /// Event fired when settings are saved to storage.
        /// </summary>
        event EventHandler<SettingsSavedEventArgs> SettingsSaved;

        /// <summary>
        /// Event fired when a setting value changes.
        /// </summary>
        event EventHandler<SettingChangedEventArgs> SettingChanged;

        /// <summary>
        /// Event fired when multiple settings are changed.
        /// </summary>
        event EventHandler<SettingsChangedEventArgs> SettingsChanged;

        /// <summary>
        /// Gets the current application settings.
        /// </summary>
        ApplicationSettings Settings { get; }

        /// <summary>
        /// Gets a value indicating whether settings have been loaded.
        /// </summary>
        bool IsLoaded { get; }

        /// <summary>
        /// Gets a value indicating whether there are unsaved changes.
        /// </summary>
        bool IsDirty { get; }

        /// <summary>
        /// Gets a value indicating whether auto-save is enabled.
        /// </summary>
        bool AutoSaveEnabled { get; set; }

        /// <summary>
        /// Loads settings from storage.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous load operation.</returns>
        Task LoadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves settings to storage.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous save operation.</returns>
        Task SaveAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Resets settings to default values.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous reset operation.</returns>
        Task ResetToDefaultsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a setting value by key.
        /// </summary>
        /// <typeparam name="T">Type of the setting value.</typeparam>
        /// <param name="key">Setting key.</param>
        /// <param name="defaultValue">Default value if key not found.</param>
        /// <returns>Setting value or default value.</returns>
        T GetSetting<T>(string key, T defaultValue = default!);

        /// <summary>
        /// Sets a setting value by key.
        /// </summary>
        /// <typeparam name="T">Type of the setting value.</typeparam>
        /// <param name="key">Setting key.</param>
        /// <param name="value">Setting value.</param>
        /// <param name="autoSave">Whether to automatically save after setting.</param>
        /// <returns>Task representing the asynchronous set operation.</returns>
        Task SetSettingAsync<T>(string key, T value, bool autoSave = true);

        /// <summary>
        /// Sets a setting value by key synchronously.
        /// </summary>
        /// <typeparam name="T">Type of the setting value.</typeparam>
        /// <param name="key">Setting key.</param>
        /// <param name="value">Setting value.</param>
        /// <param name="autoSave">Whether to automatically save after setting.</param>
        /// <returns>Task representing the asynchronous set operation.</returns>
        Task SetSetting<T>(string key, T value, bool autoSave = true);

        /// <summary>
        /// Checks if a setting exists.
        /// </summary>
        /// <param name="key">Setting key to check.</param>
        /// <returns>True if the setting exists.</returns>
        bool HasSetting(string key);

        /// <summary>
        /// Removes a setting by key.
        /// </summary>
        /// <param name="key">Setting key to remove.</param>
        /// <param name="autoSave">Whether to automatically save after removal.</param>
        /// <returns>Task representing the asynchronous remove operation.</returns>
        Task RemoveSettingAsync(string key, bool autoSave = true);

        /// <summary>
        /// Imports settings from a file.
        /// </summary>
        /// <param name="filePath">Path to the settings file.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous import operation.</returns>
        Task ImportSettingsAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Exports settings to a file.
        /// </summary>
        /// <param name="filePath">Path where to save the settings file.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous export operation.</returns>
        Task ExportSettingsAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a backup of current settings.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Path to the backup file.</returns>
        Task<string> CreateBackupAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates current settings for correctness.
        /// </summary>
        /// <returns>Validation result with any issues found.</returns>
        SettingsValidationResult ValidateSettings();

        /// <summary>
        /// Marks the settings as dirty (requiring save).
        /// </summary>
        void MarkAsDirty();
    }

    /// <summary>
    /// Event arguments for settings loaded events.
    /// </summary>
    public class SettingsLoadedEventArgs : EventArgs
    {
        /// <summary>
        /// The loaded settings.
        /// </summary>
        public ApplicationSettings Settings { get; }

        /// <summary>
        /// Whether the settings were loaded from default values.
        /// </summary>
        public bool LoadedFromDefaults { get; }

        /// <summary>
        /// Load duration in milliseconds.
        /// </summary>
        public TimeSpan LoadDuration { get; }

        public SettingsLoadedEventArgs(ApplicationSettings settings, bool loadedFromDefaults, TimeSpan loadDuration)
        {
            Settings = settings;
            LoadedFromDefaults = loadedFromDefaults;
            LoadDuration = loadDuration;
        }
    }

    /// <summary>
    /// Event arguments for settings saved events.
    /// </summary>
    public class SettingsSavedEventArgs : EventArgs
    {
        /// <summary>
        /// The saved settings.
        /// </summary>
        public ApplicationSettings Settings { get; }

        /// <summary>
        /// Save duration in milliseconds.
        /// </summary>
        public TimeSpan SaveDuration { get; }

        /// <summary>
        /// Path where settings were saved.
        /// </summary>
        public string FilePath { get; }

        public SettingsSavedEventArgs(ApplicationSettings settings, TimeSpan saveDuration, string filePath)
        {
            Settings = settings;
            SaveDuration = saveDuration;
            FilePath = filePath;
        }
    }

    /// <summary>
    /// Event arguments for setting changed events.
    /// </summary>
    public class SettingChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The setting key that changed.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// The old value.
        /// </summary>
        public object? OldValue { get; }

        /// <summary>
        /// The new value.
        /// </summary>
        public object? NewValue { get; }

        /// <summary>
        /// Type of the setting value.
        /// </summary>
        public Type ValueType { get; }

        public SettingChangedEventArgs(string key, object? oldValue, object? newValue, Type valueType)
        {
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
            ValueType = valueType;
        }
    }

    /// <summary>
    /// Result of settings validation.
    /// </summary>
    public class SettingsValidationResult
    {
        /// <summary>
        /// Whether the settings are valid.
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// List of validation errors.
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// List of validation warnings.
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>
        /// Settings that were automatically corrected.
        /// </summary>
        public Dictionary<string, object> AutoCorrected { get; set; } = new();
    }

    /// <summary>
    /// Event arguments for settings change events.
    /// </summary>
    public class SettingsChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The keys of settings that were changed.
        /// </summary>
        public List<string> ChangedKeys { get; }

        /// <summary>
        /// The reason for the settings change.
        /// </summary>
        public SettingsChangeReason Reason { get; }

        /// <summary>
        /// Whether the change was successful.
        /// </summary>
        public bool Success { get; }

        public SettingsChangedEventArgs(List<string> changedKeys, SettingsChangeReason reason = SettingsChangeReason.UserModified, bool success = true)
        {
            ChangedKeys = changedKeys ?? throw new ArgumentNullException(nameof(changedKeys));
            Reason = reason;
            Success = success;
        }
    }

    /// <summary>
    /// Reasons for settings changes.
    /// </summary>
    public enum SettingsChangeReason
    {
        /// <summary>
        /// User manually modified settings.
        /// </summary>
        UserModified,

        /// <summary>
        /// Settings were loaded from file.
        /// </summary>
        Loaded,

        /// <summary>
        /// Settings were reset to defaults.
        /// </summary>
        Reset,

        /// <summary>
        /// Settings were imported from another source.
        /// </summary>
        Imported,

        /// <summary>
        /// Settings were auto-corrected due to validation errors.
        /// </summary>
        AutoCorrected,

        /// <summary>
        /// System or external application modified the setting.
        /// </summary>
        SystemChanged
    }
}