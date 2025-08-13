using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Configuration.Interfaces;

namespace TaskbarEqualizer.Configuration.Services
{
    /// <summary>
    /// JSON-based settings manager with automatic persistence and validation.
    /// Provides thread-safe access to application configuration.
    /// </summary>
    public sealed class SettingsManager : ISettingsManager
    {
        private readonly ILogger<SettingsManager> _logger;
        private readonly string _settingsFilePath;
        private readonly string _backupDirectory;
        private readonly object _settingsLock = new();
        private readonly SemaphoreSlim _saveSemaphore = new(1, 1);
        private readonly Timer _autoSaveTimer;
        
        private ApplicationSettings _settings;
        private bool _isLoaded;
        private bool _isDirty;
        private bool _autoSaveEnabled = true;
        private bool _disposed;

        // JSON serialization options
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Initializes a new instance of the SettingsManager.
        /// </summary>
        /// <param name="logger">Logger for diagnostic information.</param>
        /// <param name="settingsFilePath">Path to the settings file (optional).</param>
        public SettingsManager(ILogger<SettingsManager> logger, string? settingsFilePath = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Set up file paths
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "TaskbarEqualizer");
            Directory.CreateDirectory(appFolder);
            
            _settingsFilePath = settingsFilePath ?? Path.Combine(appFolder, "settings.json");
            _backupDirectory = Path.Combine(appFolder, "Backups");
            Directory.CreateDirectory(_backupDirectory);

            // Initialize settings
            _settings = ApplicationSettings.CreateDefault();

            // Configure JSON serialization
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters = { new JsonStringEnumConverter() }
            };

            // Set up auto-save timer (save every 30 seconds if dirty)
            _autoSaveTimer = new Timer(AutoSaveCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            // Subscribe to settings property changes
            _settings.PropertyChanged += OnSettingsPropertyChanged;
            _settings.BulkPropertyChanged += OnSettingsBulkPropertyChanged;

            _logger.LogDebug("SettingsManager initialized with path: {SettingsPath}", _settingsFilePath);
        }

        #region Events

        /// <inheritdoc />
        public event EventHandler<SettingsLoadedEventArgs>? SettingsLoaded;

        /// <inheritdoc />
        public event EventHandler<SettingsSavedEventArgs>? SettingsSaved;

        /// <inheritdoc />
        public event EventHandler<SettingChangedEventArgs>? SettingChanged;

        /// <inheritdoc />
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion

        #region Properties

        /// <inheritdoc />
        public ApplicationSettings Settings
        {
            get
            {
                lock (_settingsLock)
                {
                    return _settings;
                }
            }
        }

        /// <inheritdoc />
        public bool IsLoaded => _isLoaded;

        /// <inheritdoc />
        public bool IsDirty => _isDirty;

        /// <inheritdoc />
        public bool AutoSaveEnabled
        {
            get => _autoSaveEnabled;
            set
            {
                _autoSaveEnabled = value;
                _logger.LogDebug("Auto-save {Status}", value ? "enabled" : "disabled");
            }
        }

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public async Task LoadAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SettingsManager));

            var stopwatch = Stopwatch.StartNew();
            bool loadedFromDefaults = false;

            _logger.LogInformation("Loading settings from {SettingsPath}", _settingsFilePath);

            try
            {
                ApplicationSettings? loadedSettings = null;

                if (File.Exists(_settingsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken);
                    
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        loadedSettings = JsonSerializer.Deserialize<ApplicationSettings>(json, _jsonOptions);
                        _logger.LogDebug("Settings deserialized from JSON");
                    }
                }

                lock (_settingsLock)
                {
                    if (loadedSettings != null)
                    {
                        // Unsubscribe from old settings
                        _settings.PropertyChanged -= OnSettingsPropertyChanged;
                        _settings.BulkPropertyChanged -= OnSettingsBulkPropertyChanged;
                        
                        _settings = loadedSettings;
                        
                        // Subscribe to new settings
                        _settings.PropertyChanged += OnSettingsPropertyChanged;
                        _settings.BulkPropertyChanged += OnSettingsBulkPropertyChanged;
                        
                        _logger.LogDebug("Settings loaded from file");
                        
                        // Migrate legacy CustomSettings to strongly-typed properties
                        MigrateLegacySettings();
                    }
                    else
                    {
                        loadedFromDefaults = true;
                        _logger.LogInformation("Settings file not found or empty, using defaults");
                    }

                    _isLoaded = true;
                    _isDirty = false;
                }

                // Validate loaded settings
                var validationResult = ValidateSettings();
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("Loaded settings failed validation, auto-correcting issues");
                    
                    foreach (var kvp in validationResult.AutoCorrected)
                    {
                        _logger.LogInformation("Auto-corrected setting {Key}: {Value}", kvp.Key, kvp.Value);
                    }

                    _isDirty = true;
                }

                stopwatch.Stop();

                // Fire loaded event
                var loadedArgs = new SettingsLoadedEventArgs(_settings, loadedFromDefaults, stopwatch.Elapsed);
                SettingsLoaded?.Invoke(this, loadedArgs);

                _logger.LogInformation("Settings loaded successfully in {Duration}ms", stopwatch.ElapsedMilliseconds);

                // Auto-save if we made corrections
                if (_isDirty && _autoSaveEnabled)
                {
                    await SaveAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings, using defaults");
                
                // Use defaults on error
                lock (_settingsLock)
                {
                    _settings = ApplicationSettings.CreateDefault();
                    _isLoaded = true;
                    _isDirty = true;
                }

                var loadedArgs = new SettingsLoadedEventArgs(_settings, true, stopwatch.Elapsed);
                SettingsLoaded?.Invoke(this, loadedArgs);

                throw;
            }
        }

        /// <inheritdoc />
        public async Task SaveAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SettingsManager));

            _logger.LogInformation("SaveAsync called - checking dirty flag. IsDirty: {IsDirty}", _isDirty);

            // Early return if not dirty
            if (!_isDirty)
            {
                _logger.LogInformation("Settings are not dirty, skipping save operation");
                return;
            }

            // Prevent concurrent saves using semaphore with timeout
            _logger.LogDebug("Waiting for save semaphore...");
            var semaphoreTimeout = TimeSpan.FromSeconds(30); // Generous timeout for semaphore
            if (!await _saveSemaphore.WaitAsync(semaphoreTimeout, cancellationToken))
            {
                _logger.LogError("Failed to acquire save semaphore within {TimeoutSeconds} seconds", semaphoreTimeout.TotalSeconds);
                throw new TimeoutException($"Settings save semaphore timeout after {semaphoreTimeout.TotalSeconds} seconds");
            }
            _logger.LogDebug("Save semaphore acquired");
            
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                _logger.LogInformation("Starting settings save operation to {SettingsPath}. IsDirty: {IsDirty}", _settingsFilePath, _isDirty);

                ApplicationSettings settingsToSave;
                
                lock (_settingsLock)
                {
                    _logger.LogDebug("Cloning settings for save operation");
                    settingsToSave = _settings.Clone();
                }

                // Serialize to JSON
                _logger.LogDebug("Serializing settings to JSON...");
                var json = JsonSerializer.Serialize(settingsToSave, _jsonOptions);
                _logger.LogDebug("Serialized settings to JSON ({JsonLength} characters)", json.Length);

                // Ensure directory exists
                var directory = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    _logger.LogDebug("Creating directory: {Directory}", directory);
                    Directory.CreateDirectory(directory);
                }

                // Write to temporary file first, then move (atomic operation)
                var tempPath = _settingsFilePath + ".tmp";
                _logger.LogDebug("Writing to temporary file: {TempPath}", tempPath);
                await File.WriteAllTextAsync(tempPath, json, cancellationToken);
                _logger.LogDebug("Temporary file written successfully");
                
                // Robust file replacement - avoid race condition with File.Exists() check
                // Use File.Copy with overwrite, then delete temp file for maximum compatibility
                const int maxRetries = 3;
                Exception? lastException = null;
                
                _logger.LogDebug("Starting file replacement operation with {MaxRetries} max retries", maxRetries);
                
                for (int attempt = 0; attempt < maxRetries; attempt++)
                {
                    try
                    {
                        _logger.LogDebug("File replacement attempt {Attempt}/{MaxRetries}", attempt + 1, maxRetries);
                        
                        // Copy temp file to final destination (overwrites if exists)
                        _logger.LogDebug("Copying {TempPath} to {SettingsPath}", tempPath, _settingsFilePath);
                        File.Copy(tempPath, _settingsFilePath, overwrite: true);
                        _logger.LogDebug("File copy successful");
                        
                        // Clean up temp file after successful copy
                        _logger.LogDebug("Deleting temporary file: {TempPath}", tempPath);
                        File.Delete(tempPath);
                        _logger.LogDebug("Temporary file deleted successfully");
                        
                        // Success - break out of retry loop
                        _logger.LogDebug("File replacement operation completed successfully");
                        break;
                    }
                    catch (IOException) when (attempt < maxRetries - 1)
                    {
                        // File might be locked or in use - retry
                        _logger.LogWarning("IO exception during file operation, retrying... (attempt {Attempt}/{MaxRetries})", 
                            attempt + 1, maxRetries);
                        lastException = null;
                        await Task.Delay(100, cancellationToken); // Brief delay before retry
                        continue;
                    }
                    catch (UnauthorizedAccessException) when (attempt < maxRetries - 1)
                    {
                        // Permission issue - retry
                        _logger.LogWarning("Access denied during file operation, retrying... (attempt {Attempt}/{MaxRetries})", 
                            attempt + 1, maxRetries);
                        lastException = null;
                        await Task.Delay(100, cancellationToken); // Brief delay before retry
                        continue;
                    }
                    catch (Exception ex) when (attempt < maxRetries - 1)
                    {
                        lastException = ex;
                        _logger.LogWarning(ex, "Exception during file operation, retrying... (attempt {Attempt}/{MaxRetries})", 
                            attempt + 1, maxRetries);
                        await Task.Delay(100, cancellationToken); // Brief delay before retry
                        continue;
                    }
                    catch (Exception ex)
                    {
                        // Final attempt failed - clean up temp file and rethrow
                        lastException = ex;
                        try
                        {
                            if (File.Exists(tempPath))
                            {
                                File.Delete(tempPath);
                            }
                        }
                        catch
                        {
                            // Ignore cleanup errors
                        }
                        break;
                    }
                }
                
                // If we have a last exception, the operation ultimately failed
                if (lastException != null)
                {
                    throw lastException;
                }

                lock (_settingsLock)
                {
                    _isDirty = false;
                }

                stopwatch.Stop();

                // Fire saved event
                var savedArgs = new SettingsSavedEventArgs(settingsToSave, stopwatch.Elapsed, _settingsFilePath);
                SettingsSaved?.Invoke(this, savedArgs);

                _logger.LogInformation("Settings saved successfully in {Duration}ms", stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings - Operation failed after {ElapsedMs}ms", stopwatch?.ElapsedMilliseconds ?? 0);
                throw;
            }
            finally
            {
                _logger.LogDebug("Releasing save semaphore");
                _saveSemaphore.Release();
            }
        }

        /// <inheritdoc />
        public async Task ResetToDefaultsAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SettingsManager));

            _logger.LogInformation("Resetting settings to defaults");

            try
            {
                lock (_settingsLock)
                {
                    // Unsubscribe from old settings
                    _settings.PropertyChanged -= OnSettingsPropertyChanged;
                    _settings.BulkPropertyChanged -= OnSettingsBulkPropertyChanged;
                    
                    // Create new default settings
                    _settings = ApplicationSettings.CreateDefault();
                    
                    // Subscribe to new settings
                    _settings.PropertyChanged += OnSettingsPropertyChanged;
                    _settings.BulkPropertyChanged += OnSettingsBulkPropertyChanged;
                    
                    _isDirty = true;
                }

                // Auto-save if enabled
                if (_autoSaveEnabled)
                {
                    await SaveAsync(cancellationToken);
                }

                _logger.LogDebug("Settings reset to defaults");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset settings to defaults");
                throw;
            }
        }

        /// <inheritdoc />
        public T GetSetting<T>(string key, T defaultValue = default!)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SettingsManager));

            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Setting key cannot be null or empty", nameof(key));

            try
            {
                lock (_settingsLock)
                {
                    if (_settings.CustomSettings.TryGetValue(key, out var value))
                    {
                        if (value is T typedValue)
                        {
                            return typedValue;
                        }
                        
                        // Try to convert the value
                        if (value != null)
                        {
                            try
                            {
                                return (T)Convert.ChangeType(value, typeof(T));
                            }
                            catch
                            {
                                _logger.LogWarning("Failed to convert setting {Key} to type {Type}", key, typeof(T).Name);
                            }
                        }
                    }

                    return defaultValue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get setting {Key}", key);
                return defaultValue;
            }
        }

        /// <inheritdoc />
        public async Task SetSettingAsync<T>(string key, T value, bool autoSave = true)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SettingsManager));

            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Setting key cannot be null or empty", nameof(key));

            try
            {
                object? oldValue = null;
                bool changed = false;

                lock (_settingsLock)
                {
                    _settings.CustomSettings.TryGetValue(key, out oldValue);
                    
                    if (!EqualityComparer<object>.Default.Equals(oldValue, value))
                    {
                        _settings.CustomSettings[key] = value!;
                        _isDirty = true;
                        changed = true;
                    }
                }

                if (changed)
                {
                    // Fire setting changed event
                    var changedArgs = new SettingChangedEventArgs(key, oldValue, value, typeof(T));
                    SettingChanged?.Invoke(this, changedArgs);

                    // Fire settings changed event for multiple settings
                    var settingsChangedArgs = new SettingsChangedEventArgs(new List<string> { key });
                    SettingsChanged?.Invoke(this, settingsChangedArgs);

                    _logger.LogDebug("Setting {Key} changed: {OldValue} -> {NewValue}", key, oldValue, value);

                    // Auto-save if requested and enabled
                    if (autoSave && _autoSaveEnabled)
                    {
                        await SaveAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set setting {Key}", key);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task SetSetting<T>(string key, T value, bool autoSave = true)
        {
            await SetSettingAsync(key, value, autoSave);
        }

        /// <summary>
        /// Marks the settings as dirty (requiring save).
        /// </summary>
        public void MarkAsDirty()
        {
            lock (_settingsLock)
            {
                _isDirty = true;
            }
            _logger.LogDebug("Settings manually marked as dirty");
        }

        /// <inheritdoc />
        public bool HasSetting(string key)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SettingsManager));

            if (string.IsNullOrEmpty(key))
                return false;

            lock (_settingsLock)
            {
                return _settings.CustomSettings.ContainsKey(key);
            }
        }

        /// <inheritdoc />
        public async Task RemoveSettingAsync(string key, bool autoSave = true)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SettingsManager));

            if (string.IsNullOrEmpty(key))
                return;

            try
            {
                bool removed = false;
                object? oldValue = null;

                lock (_settingsLock)
                {
                    if (_settings.CustomSettings.TryGetValue(key, out oldValue))
                    {
                        _settings.CustomSettings.Remove(key);
                        _isDirty = true;
                        removed = true;
                    }
                }

                if (removed)
                {
                    // Fire setting changed event
                    var changedArgs = new SettingChangedEventArgs(key, oldValue, null, typeof(object));
                    SettingChanged?.Invoke(this, changedArgs);

                    _logger.LogDebug("Setting {Key} removed", key);

                    // Auto-save if requested and enabled
                    if (autoSave && _autoSaveEnabled)
                    {
                        await SaveAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove setting {Key}", key);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task ImportSettingsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SettingsManager));

            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Settings file not found: {filePath}");

            _logger.LogInformation("Importing settings from {FilePath}", filePath);

            try
            {
                var json = await File.ReadAllTextAsync(filePath, cancellationToken);
                var importedSettings = JsonSerializer.Deserialize<ApplicationSettings>(json, _jsonOptions);

                if (importedSettings != null)
                {
                    lock (_settingsLock)
                    {
                        // Unsubscribe from old settings
                        _settings.PropertyChanged -= OnSettingsPropertyChanged;
                        _settings.BulkPropertyChanged -= OnSettingsBulkPropertyChanged;
                        
                        _settings = importedSettings;
                        
                        // Subscribe to new settings
                        _settings.PropertyChanged += OnSettingsPropertyChanged;
                        _settings.BulkPropertyChanged += OnSettingsBulkPropertyChanged;
                        
                        _isDirty = true;
                    }

                    // Validate imported settings
                    var validationResult = ValidateSettings();
                    if (!validationResult.IsValid)
                    {
                        _logger.LogWarning("Imported settings failed validation, auto-correcting issues");
                    }

                    // Auto-save if enabled
                    if (_autoSaveEnabled)
                    {
                        await SaveAsync(cancellationToken);
                    }

                    _logger.LogInformation("Settings imported successfully from {FilePath}", filePath);
                }
                else
                {
                    throw new InvalidOperationException("Failed to deserialize imported settings");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import settings from {FilePath}", filePath);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task ExportSettingsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SettingsManager));

            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            _logger.LogInformation("Exporting settings to {FilePath}", filePath);

            try
            {
                ApplicationSettings settingsToExport;
                
                lock (_settingsLock)
                {
                    settingsToExport = _settings.Clone();
                }

                var json = JsonSerializer.Serialize(settingsToExport, _jsonOptions);

                // Ensure directory exists
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(filePath, json, cancellationToken);

                _logger.LogInformation("Settings exported successfully to {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export settings to {FilePath}", filePath);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<string> CreateBackupAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SettingsManager));

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"settings_backup_{timestamp}.json";
            var backupPath = Path.Combine(_backupDirectory, backupFileName);

            _logger.LogDebug("Creating settings backup at {BackupPath}", backupPath);

            try
            {
                await ExportSettingsAsync(backupPath, cancellationToken);

                // Clean up old backups (keep last 10)
                var backupFiles = Directory.GetFiles(_backupDirectory, "settings_backup_*.json");
                if (backupFiles.Length > 10)
                {
                    Array.Sort(backupFiles);
                    for (int i = 0; i < backupFiles.Length - 10; i++)
                    {
                        try
                        {
                            File.Delete(backupFiles[i]);
                            _logger.LogDebug("Deleted old backup: {BackupFile}", backupFiles[i]);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete old backup: {BackupFile}", backupFiles[i]);
                        }
                    }
                }

                _logger.LogInformation("Settings backup created at {BackupPath}", backupPath);
                return backupPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create settings backup");
                throw;
            }
        }

        /// <inheritdoc />
        public SettingsValidationResult ValidateSettings()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SettingsManager));

            var result = new SettingsValidationResult { IsValid = true };

            try
            {
                lock (_settingsLock)
                {
                    // Validate basic settings
                    if (!_settings.IsValid())
                    {
                        result.IsValid = false;
                        result.Errors.Add("Basic settings validation failed");

                        // Auto-correct common issues
                        if (_settings.UpdateInterval <= 0)
                        {
                            _settings.UpdateInterval = 16.67; // 60 FPS
                            result.AutoCorrected["UpdateInterval"] = 16.67;
                        }

                        if (_settings.VolumeThreshold < 0 || _settings.VolumeThreshold > 1)
                        {
                            _settings.VolumeThreshold = Math.Max(0, Math.Min(1, _settings.VolumeThreshold));
                            result.AutoCorrected["VolumeThreshold"] = _settings.VolumeThreshold;
                        }

                        if (_settings.FrequencyBands < 4 || _settings.FrequencyBands > 64)
                        {
                            _settings.FrequencyBands = Math.Max(4, Math.Min(64, _settings.FrequencyBands));
                            result.AutoCorrected["FrequencyBands"] = _settings.FrequencyBands;
                        }

                        // Continue with other validations...
                        result.IsValid = result.AutoCorrected.Count == 0;
                    }

                    // Validate version compatibility
                    if (string.IsNullOrEmpty(_settings.Version))
                    {
                        result.Warnings.Add("Settings version is missing");
                        _settings.Version = "1.0.0";
                        result.AutoCorrected["Version"] = "1.0.0";
                    }

                    // Validate custom settings
                    var invalidCustomSettings = new List<string>();
                    foreach (var customSetting in _settings.CustomSettings)
                    {
                        if (customSetting.Value == null)
                        {
                            invalidCustomSettings.Add(customSetting.Key);
                        }
                    }

                    foreach (var invalidKey in invalidCustomSettings)
                    {
                        _settings.CustomSettings.Remove(invalidKey);
                        result.AutoCorrected[invalidKey] = "Removed null value";
                    }
                }

                if (result.AutoCorrected.Count > 0)
                {
                    result.IsValid = false; // Mark as invalid so caller knows corrections were made
                    _isDirty = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during settings validation");
                result.IsValid = false;
                result.Errors.Add($"Validation error: {ex.Message}");
            }

            return result;
        }

        #endregion

        #region Private Methods

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            lock (_settingsLock)
            {
                _isDirty = true;
            }

            // Fire the SettingsChanged event with the specific property that changed
            if (!string.IsNullOrEmpty(e.PropertyName))
            {
                var settingsChangedArgs = new SettingsChangedEventArgs(
                    new List<string> { e.PropertyName }, 
                    SettingsChangeReason.UserModified);
                SettingsChanged?.Invoke(this, settingsChangedArgs);
                
                _logger.LogDebug("Settings property {PropertyName} changed, fired SettingsChanged event", e.PropertyName);
            }

            OnPropertyChanged(e.PropertyName);
        }

        private void OnSettingsBulkPropertyChanged(object? sender, BulkPropertyChangedEventArgs e)
        {
            lock (_settingsLock)
            {
                _isDirty = true;
            }

            // Fire a single SettingsChanged event with all changed properties
            if (e.ChangedProperties.Count > 0)
            {
                var settingsChangedArgs = new SettingsChangedEventArgs(
                    e.ChangedProperties.ToList(), 
                    SettingsChangeReason.UserModified);
                SettingsChanged?.Invoke(this, settingsChangedArgs);
                
                _logger.LogDebug("Settings bulk changed: {ChangedProperties}, fired single SettingsChanged event", 
                    string.Join(", ", e.ChangedProperties));
            }

            // Fire PropertyChanged for the manager itself (for any listeners)
            OnPropertyChanged(nameof(Settings));
        }

        private void AutoSaveCallback(object? state)
        {
            if (_disposed || !_autoSaveEnabled || !_isDirty)
                return;

            try
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SaveAsync();
                        _logger.LogDebug("Auto-save completed");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Auto-save failed");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start auto-save task");
            }
        }

        private void OnPropertyChanged(string? propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Migration Methods
        
        /// <summary>
        /// Migrates legacy CustomSettings entries to strongly-typed properties.
        /// Removes obsolete entries to prevent conversion warnings.
        /// </summary>
        private void MigrateLegacySettings()
        {
            var legacyKeys = new[] { "RememberPosition" };
            bool migrated = false;
            
            foreach (var key in legacyKeys)
            {
                if (_settings.CustomSettings.ContainsKey(key))
                {
                    _logger.LogDebug("Removing legacy CustomSettings entry: {Key}", key);
                    _settings.CustomSettings.Remove(key);
                    migrated = true;
                }
            }
            
            if (migrated)
            {
                _isDirty = true;
                _logger.LogInformation("Migrated legacy CustomSettings entries to strongly-typed properties");
            }
        }
        
        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the settings manager and saves any pending changes.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                // Save any pending changes
                if (_isDirty && _autoSaveEnabled)
                {
                    try
                    {
                        SaveAsync().Wait(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to save settings during disposal");
                    }
                }

                // Clean up resources
                _autoSaveTimer?.Dispose();
                _saveSemaphore?.Dispose();
                
                if (_settings != null)
                {
                    _settings.PropertyChanged -= OnSettingsPropertyChanged;
                    _settings.BulkPropertyChanged -= OnSettingsBulkPropertyChanged;
                }

                _logger.LogDebug("SettingsManager disposed");
            }
        }

        #endregion
    }
}