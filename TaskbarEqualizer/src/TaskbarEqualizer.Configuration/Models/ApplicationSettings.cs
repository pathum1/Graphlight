using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.Configuration
{
    /// <summary>
    /// Application settings model with data binding support.
    /// Contains all configurable options for TaskbarEqualizer.
    /// </summary>
    public class ApplicationSettings : INotifyPropertyChanged
    {
        #region Private Fields

        private bool _startWithWindows = false;
        private bool _startMinimized = true;
        private bool _showNotifications = true;
        private bool _enableAutoUpdates = true;
        private IconSize _iconSize = IconSize.Medium;
        private EqualizerStyle _visualizationStyle = EqualizerStyle.Bars;
        private RenderQuality _renderQuality = RenderQuality.High;
        private bool _enableAnimations = true;
        private bool _enableEffects = true;
        private double _updateInterval = 16.67; // 60 FPS
        private string _selectedAudioDevice = string.Empty;
        private bool _enableAutoDeviceSwitch = true;
        private double _volumeThreshold = 0.01;
        private int _frequencyBands = 16;
        private double _smoothingFactor = 0.8;
        private double _gainFactor = 1.0;
        private string _themeName = "Default";
        private bool _followSystemTheme = true;
        private Color _customPrimaryColor = Color.FromArgb(0, 120, 215);
        private Color _customSecondaryColor = Color.FromArgb(0, 90, 158);
        private bool _useCustomColors = false;
        private bool _enableGradient = true;
        private GradientDirection _gradientDirection = GradientDirection.Vertical;
        private float _opacity = 1.0f;
        private Keys _settingsShortcut = Keys.Control | Keys.S;
        private Keys _toggleShortcut = Keys.Control | Keys.Alt | Keys.E;
        private bool _enableGlobalHotkeys = true;
        private double _animationSpeed = 1.0;
        private bool _enableBeatDetection = true;
        private bool _enableSpringPhysics = true;
        private float _springStiffness = 200.0f;
        private float _springDamping = 20.0f;
        private double _changeThreshold = 0.02;
        private bool _adaptiveQuality = true;
        private int _maxFrameRate = 60;
        private bool _vsyncEnabled = false;
        private bool _debugMode = false;
        private LogLevel _logLevel = LogLevel.Information;
        private bool _enableTelemetry = false;
        private Dictionary<string, object> _customSettings = new();

        #endregion

        #region Properties

        /// <summary>
        /// Application version for settings migration.
        /// </summary>
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// When the settings were last modified.
        /// </summary>
        public DateTime LastModified { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether to start the application with Windows.
        /// </summary>
        public bool StartWithWindows
        {
            get => _startWithWindows;
            set => SetProperty(ref _startWithWindows, value);
        }

        /// <summary>
        /// Whether to start the application minimized.
        /// </summary>
        public bool StartMinimized
        {
            get => _startMinimized;
            set => SetProperty(ref _startMinimized, value);
        }

        /// <summary>
        /// Whether to show system notifications.
        /// </summary>
        public bool ShowNotifications
        {
            get => _showNotifications;
            set => SetProperty(ref _showNotifications, value);
        }

        /// <summary>
        /// Whether to enable automatic updates.
        /// </summary>
        public bool EnableAutoUpdates
        {
            get => _enableAutoUpdates;
            set => SetProperty(ref _enableAutoUpdates, value);
        }

        #endregion

        #region Visualization Settings

        /// <summary>
        /// Size of the taskbar icon.
        /// </summary>
        public IconSize IconSize
        {
            get => _iconSize;
            set => SetProperty(ref _iconSize, value);
        }

        /// <summary>
        /// Style of the equalizer visualization.
        /// </summary>
        public EqualizerStyle VisualizationStyle
        {
            get => _visualizationStyle;
            set => SetProperty(ref _visualizationStyle, value);
        }

        /// <summary>
        /// Rendering quality level.
        /// </summary>
        public RenderQuality RenderQuality
        {
            get => _renderQuality;
            set => SetProperty(ref _renderQuality, value);
        }

        /// <summary>
        /// Whether to enable animations.
        /// </summary>
        public bool EnableAnimations
        {
            get => _enableAnimations;
            set => SetProperty(ref _enableAnimations, value);
        }

        /// <summary>
        /// Whether to enable visual effects (shadows, gradients).
        /// </summary>
        public bool EnableEffects
        {
            get => _enableEffects;
            set => SetProperty(ref _enableEffects, value);
        }

        /// <summary>
        /// Update interval in milliseconds (frame rate).
        /// </summary>
        public double UpdateInterval
        {
            get => _updateInterval;
            set => SetProperty(ref _updateInterval, Math.Max(8.33, Math.Min(1000, value))); // 1-120 FPS
        }

        #endregion

        #region Audio Settings

        /// <summary>
        /// Selected audio device ID.
        /// </summary>
        public string SelectedAudioDevice
        {
            get => _selectedAudioDevice;
            set => SetProperty(ref _selectedAudioDevice, value ?? string.Empty);
        }

        /// <summary>
        /// Whether to automatically switch to new default audio device.
        /// </summary>
        public bool EnableAutoDeviceSwitch
        {
            get => _enableAutoDeviceSwitch;
            set => SetProperty(ref _enableAutoDeviceSwitch, value);
        }

        /// <summary>
        /// Minimum volume threshold to start visualization.
        /// </summary>
        public double VolumeThreshold
        {
            get => _volumeThreshold;
            set => SetProperty(ref _volumeThreshold, Math.Max(0.0, Math.Min(1.0, value)));
        }

        /// <summary>
        /// Number of frequency bands to display.
        /// </summary>
        public int FrequencyBands
        {
            get => _frequencyBands;
            set => SetProperty(ref _frequencyBands, Math.Max(4, Math.Min(64, value)));
        }

        /// <summary>
        /// Smoothing factor for animation transitions.
        /// </summary>
        public double SmoothingFactor
        {
            get => _smoothingFactor;
            set => SetProperty(ref _smoothingFactor, Math.Max(0.0, Math.Min(1.0, value)));
        }

        /// <summary>
        /// Gain factor for amplitude scaling.
        /// </summary>
        public double GainFactor
        {
            get => _gainFactor;
            set => SetProperty(ref _gainFactor, Math.Max(0.1, Math.Min(10.0, value)));
        }

        #endregion

        #region Theme Settings

        /// <summary>
        /// Name of the current theme.
        /// </summary>
        public string ThemeName
        {
            get => _themeName;
            set => SetProperty(ref _themeName, value ?? "Default");
        }

        /// <summary>
        /// Whether to automatically follow system theme.
        /// </summary>
        public bool FollowSystemTheme
        {
            get => _followSystemTheme;
            set => SetProperty(ref _followSystemTheme, value);
        }

        /// <summary>
        /// Custom primary color (serialized as hex string).
        /// </summary>
        [JsonIgnore]
        public Color CustomPrimaryColor
        {
            get => _customPrimaryColor;
            set => SetProperty(ref _customPrimaryColor, value);
        }

        /// <summary>
        /// Custom primary color as hex string for JSON serialization.
        /// </summary>
        [JsonPropertyName("CustomPrimaryColor")]
        public string CustomPrimaryColorHex
        {
            get => ColorTranslator.ToHtml(_customPrimaryColor);
            set
            {
                try
                {
                    _customPrimaryColor = ColorTranslator.FromHtml(value);
                    OnPropertyChanged(nameof(CustomPrimaryColor));
                }
                catch
                {
                    _customPrimaryColor = Color.FromArgb(0, 120, 215);
                }
            }
        }

        /// <summary>
        /// Custom secondary color (serialized as hex string).
        /// </summary>
        [JsonIgnore]
        public Color CustomSecondaryColor
        {
            get => _customSecondaryColor;
            set => SetProperty(ref _customSecondaryColor, value);
        }

        /// <summary>
        /// Custom secondary color as hex string for JSON serialization.
        /// </summary>
        [JsonPropertyName("CustomSecondaryColor")]
        public string CustomSecondaryColorHex
        {
            get => ColorTranslator.ToHtml(_customSecondaryColor);
            set
            {
                try
                {
                    _customSecondaryColor = ColorTranslator.FromHtml(value);
                    OnPropertyChanged(nameof(CustomSecondaryColor));
                }
                catch
                {
                    _customSecondaryColor = Color.FromArgb(0, 90, 158);
                }
            }
        }

        /// <summary>
        /// Whether to use custom colors instead of theme colors.
        /// </summary>
        public bool UseCustomColors
        {
            get => _useCustomColors;
            set => SetProperty(ref _useCustomColors, value);
        }

        /// <summary>
        /// Whether to enable gradient effects.
        /// </summary>
        public bool EnableGradient
        {
            get => _enableGradient;
            set => SetProperty(ref _enableGradient, value);
        }

        /// <summary>
        /// Direction of gradient effects.
        /// </summary>
        public GradientDirection GradientDirection
        {
            get => _gradientDirection;
            set => SetProperty(ref _gradientDirection, value);
        }

        /// <summary>
        /// Overall opacity of the visualization.
        /// </summary>
        public float Opacity
        {
            get => _opacity;
            set => SetProperty(ref _opacity, Math.Max(0.1f, Math.Min(1.0f, value)));
        }

        #endregion

        #region Hotkey Settings

        /// <summary>
        /// Keyboard shortcut to open settings.
        /// </summary>
        public Keys SettingsShortcut
        {
            get => _settingsShortcut;
            set => SetProperty(ref _settingsShortcut, value);
        }

        /// <summary>
        /// Keyboard shortcut to toggle visualization.
        /// </summary>
        public Keys ToggleShortcut
        {
            get => _toggleShortcut;
            set => SetProperty(ref _toggleShortcut, value);
        }

        /// <summary>
        /// Whether to enable global hotkeys.
        /// </summary>
        public bool EnableGlobalHotkeys
        {
            get => _enableGlobalHotkeys;
            set => SetProperty(ref _enableGlobalHotkeys, value);
        }

        #endregion

        #region Advanced Settings

        /// <summary>
        /// Animation speed multiplier.
        /// </summary>
        public double AnimationSpeed
        {
            get => _animationSpeed;
            set => SetProperty(ref _animationSpeed, Math.Max(0.1, Math.Min(5.0, value)));
        }

        /// <summary>
        /// Whether to enable beat detection effects.
        /// </summary>
        public bool EnableBeatDetection
        {
            get => _enableBeatDetection;
            set => SetProperty(ref _enableBeatDetection, value);
        }

        /// <summary>
        /// Whether to enable spring physics for animations.
        /// </summary>
        public bool EnableSpringPhysics
        {
            get => _enableSpringPhysics;
            set => SetProperty(ref _enableSpringPhysics, value);
        }

        /// <summary>
        /// Spring stiffness for physics simulation.
        /// </summary>
        public float SpringStiffness
        {
            get => _springStiffness;
            set => SetProperty(ref _springStiffness, Math.Max(10.0f, Math.Min(1000.0f, value)));
        }

        /// <summary>
        /// Spring damping for physics simulation.
        /// </summary>
        public float SpringDamping
        {
            get => _springDamping;
            set => SetProperty(ref _springDamping, Math.Max(1.0f, Math.Min(100.0f, value)));
        }

        /// <summary>
        /// Minimum change threshold to trigger re-rendering.
        /// </summary>
        public double ChangeThreshold
        {
            get => _changeThreshold;
            set => SetProperty(ref _changeThreshold, Math.Max(0.001, Math.Min(0.5, value)));
        }

        /// <summary>
        /// Whether to enable adaptive quality based on performance.
        /// </summary>
        public bool AdaptiveQuality
        {
            get => _adaptiveQuality;
            set => SetProperty(ref _adaptiveQuality, value);
        }

        /// <summary>
        /// Maximum frame rate for rendering.
        /// </summary>
        public int MaxFrameRate
        {
            get => _maxFrameRate;
            set => SetProperty(ref _maxFrameRate, Math.Max(15, Math.Min(144, value)));
        }

        /// <summary>
        /// Whether to enable V-Sync.
        /// </summary>
        public bool VsyncEnabled
        {
            get => _vsyncEnabled;
            set => SetProperty(ref _vsyncEnabled, value);
        }

        #endregion

        #region Developer Settings

        /// <summary>
        /// Whether debug mode is enabled.
        /// </summary>
        public bool DebugMode
        {
            get => _debugMode;
            set => SetProperty(ref _debugMode, value);
        }

        /// <summary>
        /// Logging level.
        /// </summary>
        public LogLevel LogLevel
        {
            get => _logLevel;
            set => SetProperty(ref _logLevel, value);
        }

        /// <summary>
        /// Whether to enable telemetry.
        /// </summary>
        public bool EnableTelemetry
        {
            get => _enableTelemetry;
            set => SetProperty(ref _enableTelemetry, value);
        }

        #endregion

        #region Custom Settings

        /// <summary>
        /// Dictionary for storing custom settings.
        /// </summary>
        public Dictionary<string, object> CustomSettings
        {
            get => _customSettings;
            set => SetProperty(ref _customSettings, value ?? new Dictionary<string, object>());
        }

        #endregion

        #region Events

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion

        #region Methods

        /// <summary>
        /// Creates a default settings instance.
        /// </summary>
        /// <returns>New settings with default values.</returns>
        public static ApplicationSettings CreateDefault()
        {
            return new ApplicationSettings();
        }

        /// <summary>
        /// Creates a copy of these settings.
        /// </summary>
        /// <returns>Deep copy of the settings.</returns>
        public ApplicationSettings Clone()
        {
            var clone = new ApplicationSettings();
            CopyTo(clone);
            return clone;
        }

        /// <summary>
        /// Copies settings values to another instance.
        /// </summary>
        /// <param name="target">Target settings instance.</param>
        public void CopyTo(ApplicationSettings target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            target.Version = Version;
            target.LastModified = LastModified;
            target.StartWithWindows = StartWithWindows;
            target.StartMinimized = StartMinimized;
            target.ShowNotifications = ShowNotifications;
            target.EnableAutoUpdates = EnableAutoUpdates;
            target.IconSize = IconSize;
            target.VisualizationStyle = VisualizationStyle;
            target.RenderQuality = RenderQuality;
            target.EnableAnimations = EnableAnimations;
            target.EnableEffects = EnableEffects;
            target.UpdateInterval = UpdateInterval;
            target.SelectedAudioDevice = SelectedAudioDevice;
            target.EnableAutoDeviceSwitch = EnableAutoDeviceSwitch;
            target.VolumeThreshold = VolumeThreshold;
            target.FrequencyBands = FrequencyBands;
            target.SmoothingFactor = SmoothingFactor;
            target.GainFactor = GainFactor;
            target.ThemeName = ThemeName;
            target.FollowSystemTheme = FollowSystemTheme;
            target.CustomPrimaryColor = CustomPrimaryColor;
            target.CustomSecondaryColor = CustomSecondaryColor;
            target.UseCustomColors = UseCustomColors;
            target.EnableGradient = EnableGradient;
            target.GradientDirection = GradientDirection;
            target.Opacity = Opacity;
            target.SettingsShortcut = SettingsShortcut;
            target.ToggleShortcut = ToggleShortcut;
            target.EnableGlobalHotkeys = EnableGlobalHotkeys;
            target.AnimationSpeed = AnimationSpeed;
            target.EnableBeatDetection = EnableBeatDetection;
            target.EnableSpringPhysics = EnableSpringPhysics;
            target.SpringStiffness = SpringStiffness;
            target.SpringDamping = SpringDamping;
            target.ChangeThreshold = ChangeThreshold;
            target.AdaptiveQuality = AdaptiveQuality;
            target.MaxFrameRate = MaxFrameRate;
            target.VsyncEnabled = VsyncEnabled;
            target.DebugMode = DebugMode;
            target.LogLevel = LogLevel;
            target.EnableTelemetry = EnableTelemetry;
            target.CustomSettings = new Dictionary<string, object>(CustomSettings);
        }

        /// <summary>
        /// Validates the settings for correctness.
        /// </summary>
        /// <returns>True if settings are valid.</returns>
        public bool IsValid()
        {
            return UpdateInterval > 0 &&
                   VolumeThreshold >= 0 && VolumeThreshold <= 1 &&
                   FrequencyBands >= 4 && FrequencyBands <= 64 &&
                   SmoothingFactor >= 0 && SmoothingFactor <= 1 &&
                   GainFactor > 0 && GainFactor <= 10 &&
                   Opacity > 0 && Opacity <= 1 &&
                   AnimationSpeed > 0 && AnimationSpeed <= 5 &&
                   SpringStiffness >= 10 && SpringStiffness <= 1000 &&
                   SpringDamping >= 1 && SpringDamping <= 100 &&
                   ChangeThreshold > 0 && ChangeThreshold <= 0.5 &&
                   MaxFrameRate >= 15 && MaxFrameRate <= 144;
        }

        /// <summary>
        /// Sets a property value and notifies of changes.
        /// </summary>
        /// <typeparam name="T">Type of the property.</typeparam>
        /// <param name="field">Reference to the backing field.</param>
        /// <param name="value">New value to set.</param>
        /// <param name="propertyName">Name of the property (auto-filled).</param>
        /// <returns>True if the value was changed.</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            LastModified = DateTime.UtcNow;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// Raises the PropertyChanged event.
        /// </summary>
        /// <param name="propertyName">Name of the property that changed.</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    /// <summary>
    /// Logging levels for the application.
    /// </summary>
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Information = 2,
        Warning = 3,
        Error = 4,
        Critical = 5,
        None = 6
    }
}