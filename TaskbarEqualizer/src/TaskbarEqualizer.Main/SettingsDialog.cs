using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using TaskbarEqualizer.Configuration;
using TaskbarEqualizer.Configuration.Interfaces;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.Main
{
    /// <summary>
    /// Modern Windows Forms settings dialog for Graphlight (TaskbarEqualizer).
    /// Provides comprehensive configuration options with real-time preview.
    /// </summary>
    public partial class SettingsDialog : Form
    {
        private readonly ISettingsManager _settingsManager;
        private readonly ILogger<SettingsDialog> _logger;
        private readonly IServiceProvider _serviceProvider;
        private ApplicationSettings _settings;
        private ApplicationSettings _originalSettings;
        
        // Flag to prevent infinite event loops during dialog operations
        private bool _isApplyingSettings;

        // UI Controls
        private TabControl _tabControl = null!;
        private TableLayoutPanel _mainLayout = null!;
        private Button _okButton = null!;
        private Button _cancelButton = null!;
        private Button _applyButton = null!;
        private Button _resetButton = null!;

        // General Tab Controls
        private CheckBox _startWithWindowsCheckBox = null!;
        private CheckBox _startMinimizedCheckBox = null!;
        private CheckBox _showNotificationsCheckBox = null!;
        private CheckBox _rememberPositionCheckBox = null!;

        // Visualization Tab Controls
        private ComboBox _visualizationStyleComboBox = null!;
        private ComboBox _renderQualityComboBox = null!;
        private CheckBox _enableAnimationsCheckBox = null!;
        private CheckBox _enableEffectsCheckBox = null!;
        private TrackBar _opacityTrackBar = null!;
        private Label _opacityValueLabel = null!;

        // Color Tab Controls
        private CheckBox _useCustomColorsCheckBox = null!;
        private CheckBox _enableGradientCheckBox = null!;
        private ComboBox _gradientDirectionComboBox = null!;
        private Button _primaryColorButton = null!;
        private Button _secondaryColorButton = null!;
        private Panel _primaryColorPreview = null!;
        private Panel _secondaryColorPreview = null!;
        private Panel _gradientPreview = null!;

        // Audio Tab Controls
        private ComboBox _audioDeviceComboBox = null!;
        private CheckBox _enableAutoDeviceSwitchCheckBox = null!;
        private TrackBar _frequencyBandsTrackBar = null!;
        private Label _frequencyBandsValueLabel = null!;
        private TrackBar _smoothingFactorTrackBar = null!;
        private Label _smoothingFactorValueLabel = null!;
        private TrackBar _gainFactorTrackBar = null!;
        private Label _gainFactorValueLabel = null!;
        private TrackBar _volumeThresholdTrackBar = null!;
        private Label _volumeThresholdValueLabel = null!;

        // About Tab Controls
        private Label _appNameLabel = null!;
        private Label _versionLabel = null!;
        private Label _descriptionLabel = null!;
        private Button _donationButton = null!;

        public SettingsDialog(ISettingsManager settingsManager, ILogger<SettingsDialog> logger, IServiceProvider serviceProvider)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            
            // Work with a copy of the settings to avoid triggering events during dialog operations
            _originalSettings = _settingsManager.Settings.Clone();
            _settings = _originalSettings.Clone(); // Working copy for the dialog

            InitializeComponent();
            ApplyModernStyling();
            LoadSettings();
            
            _logger.LogInformation("Settings dialog initialized with temporary settings copy");
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            // Form properties
            Text = "Graphlight Settings";
            Size = new Size(600, 500);
            MinimumSize = new Size(500, 400);
            MaximizeBox = false;
            MinimizeBox = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Icon = SystemIcons.Application;

            // Main layout
            _mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Margin = new Padding(0)
            };
            _mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));

            // Tab control
            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(8)
            };

            CreateGeneralTab();
            CreateVisualizationTab();
            CreateColorTab();
            CreateAudioTab();
            CreateAboutTab();

            _mainLayout.Controls.Add(_tabControl, 0, 0);

            // Button panel
            var buttonPanel = CreateButtonPanel();
            _mainLayout.Controls.Add(buttonPanel, 0, 1);

            Controls.Add(_mainLayout);
            ResumeLayout(false);
        }

        private void CreateGeneralTab()
        {
            var tabPage = new TabPage("General");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 6,
                ColumnCount = 1,
                Padding = new Padding(10)
            };

            // Row styles
            for (int i = 0; i < 5; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Start with Windows
            _startWithWindowsCheckBox = new CheckBox
            {
                Text = "Start with Windows",
                AutoSize = true,
                Dock = DockStyle.Left
            };
            _startWithWindowsCheckBox.CheckedChanged += OnStartWithWindowsChanged;

            // Start minimized
            _startMinimizedCheckBox = new CheckBox
            {
                Text = "Start minimized",
                AutoSize = true,
                Dock = DockStyle.Left
            };
            _startMinimizedCheckBox.CheckedChanged += OnStartMinimizedChanged;

            // Show notifications
            _showNotificationsCheckBox = new CheckBox
            {
                Text = "Show system notifications",
                AutoSize = true,
                Dock = DockStyle.Left
            };
            _showNotificationsCheckBox.CheckedChanged += OnShowNotificationsChanged;

            // Remember position
            _rememberPositionCheckBox = new CheckBox
            {
                Text = "Remember window position when moved",
                AutoSize = true,
                Dock = DockStyle.Left
            };
            _rememberPositionCheckBox.CheckedChanged += OnRememberPositionChanged;

            layout.Controls.Add(_startWithWindowsCheckBox, 0, 0);
            layout.Controls.Add(_startMinimizedCheckBox, 0, 1);
            layout.Controls.Add(_showNotificationsCheckBox, 0, 2);
            layout.Controls.Add(_rememberPositionCheckBox, 0, 3);

            tabPage.Controls.Add(layout);
            _tabControl.TabPages.Add(tabPage);
        }

        private void CreateVisualizationTab()
        {
            var tabPage = new TabPage("Visualization");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 6,
                ColumnCount = 2,
                Padding = new Padding(10)
            };

            // Configure columns
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            // Configure rows
            for (int i = 0; i < 5; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Visualization Style
            layout.Controls.Add(new Label { Text = "Style:", TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            _visualizationStyleComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill
            };
            _visualizationStyleComboBox.Items.AddRange(Enum.GetNames(typeof(EqualizerStyle)));
            _visualizationStyleComboBox.SelectedIndexChanged += OnVisualizationStyleChanged;
            layout.Controls.Add(_visualizationStyleComboBox, 1, 0);

            // Render Quality
            layout.Controls.Add(new Label { Text = "Quality:", TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            _renderQualityComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill
            };
            _renderQualityComboBox.Items.AddRange(Enum.GetNames(typeof(RenderQuality)));
            _renderQualityComboBox.SelectedIndexChanged += OnRenderQualityChanged;
            layout.Controls.Add(_renderQualityComboBox, 1, 1);

            // Enable Animations
            layout.Controls.Add(new Label { Text = "Animations:", TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            _enableAnimationsCheckBox = new CheckBox
            {
                Text = "Enable animations",
                Dock = DockStyle.Left
            };
            _enableAnimationsCheckBox.CheckedChanged += OnEnableAnimationsChanged;
            layout.Controls.Add(_enableAnimationsCheckBox, 1, 2);

            // Enable Effects
            layout.Controls.Add(new Label { Text = "Effects:", TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            _enableEffectsCheckBox = new CheckBox
            {
                Text = "Enable visual effects",
                Dock = DockStyle.Left
            };
            _enableEffectsCheckBox.CheckedChanged += OnEnableEffectsChanged;
            layout.Controls.Add(_enableEffectsCheckBox, 1, 3);

            // Opacity
            layout.Controls.Add(new Label { Text = "Opacity:", TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            var opacityPanel = new Panel { Dock = DockStyle.Fill };
            _opacityTrackBar = new TrackBar
            {
                Minimum = 10,
                Maximum = 100,
                TickFrequency = 10,
                Dock = DockStyle.Left,
                Width = 200
            };
            _opacityValueLabel = new Label
            {
                Text = "100%",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Right,
                Width = 50
            };
            _opacityTrackBar.ValueChanged += OnOpacityChanged;
            opacityPanel.Controls.Add(_opacityTrackBar);
            opacityPanel.Controls.Add(_opacityValueLabel);
            layout.Controls.Add(opacityPanel, 1, 4);

            tabPage.Controls.Add(layout);
            _tabControl.TabPages.Add(tabPage);
        }

        private void CreateColorTab()
        {
            var tabPage = new TabPage("Colors");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 8,
                ColumnCount = 2,
                Padding = new Padding(10)
            };

            // Configure columns
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            // Configure rows
            for (int i = 0; i < 7; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Use Custom Colors
            layout.Controls.Add(new Label { Text = "Custom Colors:", TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            _useCustomColorsCheckBox = new CheckBox
            {
                Text = "Use custom colors",
                Dock = DockStyle.Left
            };
            _useCustomColorsCheckBox.CheckedChanged += OnUseCustomColorsChanged;
            layout.Controls.Add(_useCustomColorsCheckBox, 1, 0);

            // Primary Color
            layout.Controls.Add(new Label { Text = "Primary Color:", TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            var primaryColorPanel = new Panel { Dock = DockStyle.Fill, Height = 30 };
            _primaryColorButton = new Button
            {
                Text = "Choose Color...",
                Dock = DockStyle.Left,
                Width = 100
            };
            _primaryColorPreview = new Panel
            {
                Dock = DockStyle.Right,
                Width = 30,
                BorderStyle = BorderStyle.FixedSingle
            };
            _primaryColorButton.Click += OnPrimaryColorClick;
            primaryColorPanel.Controls.Add(_primaryColorButton);
            primaryColorPanel.Controls.Add(_primaryColorPreview);
            layout.Controls.Add(primaryColorPanel, 1, 1);

            // Secondary Color
            layout.Controls.Add(new Label { Text = "Secondary Color:", TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            var secondaryColorPanel = new Panel { Dock = DockStyle.Fill, Height = 30 };
            _secondaryColorButton = new Button
            {
                Text = "Choose Color...",
                Dock = DockStyle.Left,
                Width = 100
            };
            _secondaryColorPreview = new Panel
            {
                Dock = DockStyle.Right,
                Width = 30,
                BorderStyle = BorderStyle.FixedSingle
            };
            _secondaryColorButton.Click += OnSecondaryColorClick;
            secondaryColorPanel.Controls.Add(_secondaryColorButton);
            secondaryColorPanel.Controls.Add(_secondaryColorPreview);
            layout.Controls.Add(secondaryColorPanel, 1, 2);

            // Enable Gradient
            layout.Controls.Add(new Label { Text = "Gradient:", TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            _enableGradientCheckBox = new CheckBox
            {
                Text = "Enable gradient effects",
                Dock = DockStyle.Left
            };
            _enableGradientCheckBox.CheckedChanged += OnEnableGradientChanged;
            layout.Controls.Add(_enableGradientCheckBox, 1, 3);

            // Gradient Direction
            layout.Controls.Add(new Label { Text = "Direction:", TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            _gradientDirectionComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill
            };
            _gradientDirectionComboBox.Items.AddRange(Enum.GetNames(typeof(GradientDirection)));
            _gradientDirectionComboBox.SelectedIndexChanged += OnGradientDirectionChanged;
            layout.Controls.Add(_gradientDirectionComboBox, 1, 4);

            // Gradient Preview
            layout.Controls.Add(new Label { Text = "Preview:", TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
            _gradientPreview = new Panel
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Height = 50
            };
            _gradientPreview.Paint += OnGradientPreviewPaint;
            layout.Controls.Add(_gradientPreview, 1, 5);

            tabPage.Controls.Add(layout);
            _tabControl.TabPages.Add(tabPage);
        }

        private void CreateAudioTab()
        {
            var tabPage = new TabPage("Audio");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 8,
                ColumnCount = 2,
                Padding = new Padding(10)
            };

            // Configure columns
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            // Configure rows
            for (int i = 0; i < 7; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Audio Device
            layout.Controls.Add(new Label { Text = "Audio Device:", TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            _audioDeviceComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill
            };
            _audioDeviceComboBox.Items.Add("Default System Device");
            _audioDeviceComboBox.SelectedIndexChanged += OnAudioDeviceChanged;
            layout.Controls.Add(_audioDeviceComboBox, 1, 0);

            // Auto Device Switch
            layout.Controls.Add(new Label { Text = "Auto Switch:", TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            _enableAutoDeviceSwitchCheckBox = new CheckBox
            {
                Text = "Automatically switch to new default device",
                Dock = DockStyle.Left
            };
            _enableAutoDeviceSwitchCheckBox.CheckedChanged += OnEnableAutoDeviceSwitchChanged;
            layout.Controls.Add(_enableAutoDeviceSwitchCheckBox, 1, 1);

            // Frequency Bands
            layout.Controls.Add(new Label { Text = "Frequency Bands:", TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            var bandsPanel = CreateSliderPanel(out _frequencyBandsTrackBar, out _frequencyBandsValueLabel, 4, 64, "");
            _frequencyBandsTrackBar.ValueChanged += OnFrequencyBandsChanged;
            layout.Controls.Add(bandsPanel, 1, 2);

            // Smoothing Factor
            layout.Controls.Add(new Label { Text = "Smoothing:", TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            var smoothingPanel = CreateSliderPanel(out _smoothingFactorTrackBar, out _smoothingFactorValueLabel, 0, 100, "%");
            _smoothingFactorTrackBar.ValueChanged += OnSmoothingFactorChanged;
            layout.Controls.Add(smoothingPanel, 1, 3);

            // Gain Factor
            layout.Controls.Add(new Label { Text = "Gain:", TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            var gainPanel = CreateSliderPanel(out _gainFactorTrackBar, out _gainFactorValueLabel, 10, 1000, "%");
            _gainFactorTrackBar.ValueChanged += OnGainFactorChanged;
            layout.Controls.Add(gainPanel, 1, 4);

            // Volume Threshold
            layout.Controls.Add(new Label { Text = "Threshold:", TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
            var thresholdPanel = CreateSliderPanel(out _volumeThresholdTrackBar, out _volumeThresholdValueLabel, 0, 100, "%");
            _volumeThresholdTrackBar.ValueChanged += OnVolumeThresholdChanged;
            layout.Controls.Add(thresholdPanel, 1, 5);

            tabPage.Controls.Add(layout);
            _tabControl.TabPages.Add(tabPage);
        }

        private Panel CreateSliderPanel(out TrackBar trackBar, out Label valueLabel, int min, int max, string suffix)
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            
            trackBar = new TrackBar
            {
                Minimum = min,
                Maximum = max,
                TickFrequency = Math.Max(1, (max - min) / 10),
                Dock = DockStyle.Left,
                Width = 200
            };

            valueLabel = new Label
            {
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Right,
                Width = 60
            };

            panel.Controls.Add(trackBar);
            panel.Controls.Add(valueLabel);
            
            return panel;
        }

        private void CreateAboutTab()
        {
            var tabPage = new TabPage("About");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 6,
                ColumnCount = 1,
                Padding = new Padding(20)
            };

            // Configure rows
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // App Name
            _appNameLabel = new Label
            {
                Text = "Graphlight (TaskbarEqualizer)",
                Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            layout.Controls.Add(_appNameLabel, 0, 0);

            // Version
            _versionLabel = new Label
            {
                Text = $"Version {_settings.Version}",
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            layout.Controls.Add(_versionLabel, 0, 1);

            // Description
            _descriptionLabel = new Label
            {
                Text = "Professional audio visualizer for Windows taskbar\nReal-time spectrum analysis with modern design",
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            layout.Controls.Add(_descriptionLabel, 0, 2);

            // Donation Button
            _donationButton = new Button
            {
                Text = "☕ Support Development (Ko-Fi)",
                Size = new Size(200, 30),
                Anchor = AnchorStyles.None,
                BackColor = Color.FromArgb(255, 95, 135),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _donationButton.FlatAppearance.BorderSize = 0;
            _donationButton.Click += OnDonationClick;
            layout.Controls.Add(_donationButton, 0, 3);

            // Copyright
            var copyrightLabel = new Label
            {
                Text = "© 2024 Graphlight. All rights reserved.",
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                ForeColor = SystemColors.GrayText
            };
            layout.Controls.Add(copyrightLabel, 0, 4);

            tabPage.Controls.Add(layout);
            _tabControl.TabPages.Add(tabPage);
        }

        private Panel CreateButtonPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var buttonLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                ColumnCount = 4,
                RowCount = 1
            };

            // Configure columns for buttons
            for (int i = 0; i < 4; i++)
                buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));

            _resetButton = new Button
            {
                Text = "Reset",
                Size = new Size(75, 30),
                DialogResult = DialogResult.None
            };
            _resetButton.Click += OnResetClick;

            _applyButton = new Button
            {
                Text = "Apply",
                Size = new Size(75, 30),
                DialogResult = DialogResult.None
            };
            _applyButton.Click += OnApplyClick;

            _cancelButton = new Button
            {
                Text = "Cancel",
                Size = new Size(75, 30),
                DialogResult = DialogResult.Cancel
            };

            _okButton = new Button
            {
                Text = "OK",
                Size = new Size(75, 30),
                DialogResult = DialogResult.OK
            };

            buttonLayout.Controls.Add(_resetButton, 0, 0);
            buttonLayout.Controls.Add(_applyButton, 1, 0);
            buttonLayout.Controls.Add(_cancelButton, 2, 0);
            buttonLayout.Controls.Add(_okButton, 3, 0);

            panel.Controls.Add(buttonLayout);
            return panel;
        }

        private void ApplyModernStyling()
        {
            // Enable visual styles
            Application.EnableVisualStyles();
            
            // Set modern colors
            BackColor = SystemColors.Window;
            ForeColor = SystemColors.WindowText;

            // Tab control styling
            _tabControl.Appearance = TabAppearance.Normal;
            _tabControl.SizeMode = TabSizeMode.Normal;
        }

        private void LoadSettings()
        {
            try
            {
                // General Tab
                _startWithWindowsCheckBox.Checked = _settings.StartWithWindows;
                _startMinimizedCheckBox.Checked = _settings.StartMinimized;
                _showNotificationsCheckBox.Checked = _settings.ShowNotifications;
                _rememberPositionCheckBox.Checked = _settingsManager.GetSetting("RememberPosition", false);

                // Visualization Tab
                _visualizationStyleComboBox.SelectedItem = _settings.VisualizationStyle.ToString();
                _renderQualityComboBox.SelectedItem = _settings.RenderQuality.ToString();
                _enableAnimationsCheckBox.Checked = _settings.EnableAnimations;
                _enableEffectsCheckBox.Checked = _settings.EnableEffects;
                _opacityTrackBar.Value = (int)(_settings.Opacity * 100);
                _opacityValueLabel.Text = $"{_opacityTrackBar.Value}%";

                // Color Tab
                _useCustomColorsCheckBox.Checked = _settings.UseCustomColors;
                _enableGradientCheckBox.Checked = _settings.EnableGradient;
                _gradientDirectionComboBox.SelectedItem = _settings.GradientDirection.ToString();
                _primaryColorPreview.BackColor = _settings.CustomPrimaryColor;
                _secondaryColorPreview.BackColor = _settings.CustomSecondaryColor;
                UpdateColorControlsEnabled();

                // Audio Tab
                LoadAudioDevices();
                _enableAutoDeviceSwitchCheckBox.Checked = _settings.EnableAutoDeviceSwitch;
                _frequencyBandsTrackBar.Value = _settings.FrequencyBands;
                _frequencyBandsValueLabel.Text = _settings.FrequencyBands.ToString();
                _smoothingFactorTrackBar.Value = (int)(_settings.SmoothingFactor * 100);
                _smoothingFactorValueLabel.Text = $"{_smoothingFactorTrackBar.Value}%";
                _gainFactorTrackBar.Value = (int)(_settings.GainFactor * 100);
                _gainFactorValueLabel.Text = $"{_gainFactorTrackBar.Value}%";
                _volumeThresholdTrackBar.Value = (int)(_settings.VolumeThreshold * 100);
                _volumeThresholdValueLabel.Text = $"{_volumeThresholdTrackBar.Value}%";

                _gradientPreview.Invalidate();
                _logger.LogDebug("Settings loaded into dialog controls");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings into dialog");
                MessageBox.Show($"Failed to load settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #region Event Handlers

        private void OnStartWithWindowsChanged(object? sender, EventArgs e)
        {
            _settings.StartWithWindows = _startWithWindowsCheckBox.Checked;
            UpdateApplyButton();
        }

        private void OnStartMinimizedChanged(object? sender, EventArgs e)
        {
            _settings.StartMinimized = _startMinimizedCheckBox.Checked;
            UpdateApplyButton();
        }

        private void OnShowNotificationsChanged(object? sender, EventArgs e)
        {
            _settings.ShowNotifications = _showNotificationsCheckBox.Checked;
            UpdateApplyButton();
        }

        private void OnRememberPositionChanged(object? sender, EventArgs e)
        {
            var value = _rememberPositionCheckBox.Checked;
            Task.Run(async () => 
            {
                try 
                {
                    await _settingsManager.SetSettingAsync("RememberPosition", value, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to set RememberPosition setting");
                }
            });
            _logger.LogDebug("Remember position changed: {Value}", value);
        }

        private void OnVisualizationStyleChanged(object? sender, EventArgs e)
        {
            if (Enum.TryParse<EqualizerStyle>(_visualizationStyleComboBox.SelectedItem?.ToString(), out var style))
            {
                _settings.VisualizationStyle = style;
                UpdateApplyButton();
            }
        }

        private void OnRenderQualityChanged(object? sender, EventArgs e)
        {
            if (Enum.TryParse<RenderQuality>(_renderQualityComboBox.SelectedItem?.ToString(), out var quality))
            {
                _settings.RenderQuality = quality;
                UpdateApplyButton();
            }
        }

        private void OnEnableAnimationsChanged(object? sender, EventArgs e)
        {
            _settings.EnableAnimations = _enableAnimationsCheckBox.Checked;
            UpdateApplyButton();
        }

        private void OnEnableEffectsChanged(object? sender, EventArgs e)
        {
            _settings.EnableEffects = _enableEffectsCheckBox.Checked;
            UpdateApplyButton();
        }

        private void OnOpacityChanged(object? sender, EventArgs e)
        {
            _settings.Opacity = _opacityTrackBar.Value / 100.0f;
            _opacityValueLabel.Text = $"{_opacityTrackBar.Value}%";
            UpdateApplyButton();
        }

        private void OnUseCustomColorsChanged(object? sender, EventArgs e)
        {
            _settings.UseCustomColors = _useCustomColorsCheckBox.Checked;
            UpdateColorControlsEnabled();
            _gradientPreview.Invalidate();
            UpdateApplyButton();
        }

        private void OnPrimaryColorClick(object? sender, EventArgs e)
        {
            using var colorDialog = new ColorDialog
            {
                Color = _settings.CustomPrimaryColor,
                FullOpen = true
            };

            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                _settings.CustomPrimaryColor = colorDialog.Color;
                _primaryColorPreview.BackColor = colorDialog.Color;
                _gradientPreview.Invalidate();
                UpdateApplyButton();
            }
        }

        private void OnSecondaryColorClick(object? sender, EventArgs e)
        {
            using var colorDialog = new ColorDialog
            {
                Color = _settings.CustomSecondaryColor,
                FullOpen = true
            };

            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                _settings.CustomSecondaryColor = colorDialog.Color;
                _secondaryColorPreview.BackColor = colorDialog.Color;
                _gradientPreview.Invalidate();
                UpdateApplyButton();
            }
        }

        private void OnEnableGradientChanged(object? sender, EventArgs e)
        {
            _settings.EnableGradient = _enableGradientCheckBox.Checked;
            _gradientDirectionComboBox.Enabled = _enableGradientCheckBox.Checked;
            _gradientPreview.Invalidate();
            UpdateApplyButton();
        }

        private void OnGradientDirectionChanged(object? sender, EventArgs e)
        {
            if (Enum.TryParse<GradientDirection>(_gradientDirectionComboBox.SelectedItem?.ToString(), out var direction))
            {
                _settings.GradientDirection = direction;
                _gradientPreview.Invalidate();
                UpdateApplyButton();
            }
        }

        private void OnGradientPreviewPaint(object? sender, PaintEventArgs e)
        {
            var rect = _gradientPreview.ClientRectangle;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            // Fill background first
            using (var bgBrush = new SolidBrush(SystemColors.Window))
            {
                e.Graphics.FillRectangle(bgBrush, rect);
            }

            // Apply gradient or solid color
            if (_settings.EnableGradient && _settings.UseCustomColors)
            {
                using var brush = _settings.GradientDirection switch
                {
                    GradientDirection.Horizontal => new System.Drawing.Drawing2D.LinearGradientBrush(
                        new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top),
                        _settings.CustomPrimaryColor, _settings.CustomSecondaryColor),
                    GradientDirection.Vertical => new System.Drawing.Drawing2D.LinearGradientBrush(
                        new Point(rect.Left, rect.Top), new Point(rect.Left, rect.Bottom),
                        _settings.CustomPrimaryColor, _settings.CustomSecondaryColor),
                    GradientDirection.Diagonal => new System.Drawing.Drawing2D.LinearGradientBrush(
                        new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Bottom),
                        _settings.CustomPrimaryColor, _settings.CustomSecondaryColor),
                    _ => (Brush)new SolidBrush(_settings.CustomPrimaryColor)
                };
                e.Graphics.FillRectangle(brush, rect);
            }
            else if (_settings.UseCustomColors)
            {
                using var brush = new SolidBrush(_settings.CustomPrimaryColor);
                e.Graphics.FillRectangle(brush, rect);
            }

            // Draw border
            using var pen = new Pen(SystemColors.ControlDark);
            e.Graphics.DrawRectangle(pen, 0, 0, rect.Width - 1, rect.Height - 1);
        }

        private void OnFrequencyBandsChanged(object? sender, EventArgs e)
        {
            _settings.FrequencyBands = _frequencyBandsTrackBar.Value;
            _frequencyBandsValueLabel.Text = _settings.FrequencyBands.ToString();
            UpdateApplyButton();
        }

        private void OnSmoothingFactorChanged(object? sender, EventArgs e)
        {
            _settings.SmoothingFactor = _smoothingFactorTrackBar.Value / 100.0;
            _smoothingFactorValueLabel.Text = $"{_smoothingFactorTrackBar.Value}%";
            UpdateApplyButton();
        }

        private void OnGainFactorChanged(object? sender, EventArgs e)
        {
            _settings.GainFactor = _gainFactorTrackBar.Value / 100.0;
            _gainFactorValueLabel.Text = $"{_gainFactorTrackBar.Value}%";
            UpdateApplyButton();
        }

        private void OnVolumeThresholdChanged(object? sender, EventArgs e)
        {
            _settings.VolumeThreshold = _volumeThresholdTrackBar.Value / 100.0;
            _volumeThresholdValueLabel.Text = $"{_volumeThresholdTrackBar.Value}%";
            UpdateApplyButton();
        }

        private void OnAudioDeviceChanged(object? sender, EventArgs e)
        {
            if (_audioDeviceComboBox.SelectedIndex == 0)
            {
                _settings.SelectedAudioDevice = string.Empty; // Default device
            }
            else if (_audioDeviceComboBox.Tag is Dictionary<string, string> deviceMap && 
                     _audioDeviceComboBox.SelectedItem is string selectedName && 
                     deviceMap.TryGetValue(selectedName, out var deviceId))
            {
                _settings.SelectedAudioDevice = deviceId;
            }
            UpdateApplyButton();
        }

        private void OnEnableAutoDeviceSwitchChanged(object? sender, EventArgs e)
        {
            _settings.EnableAutoDeviceSwitch = _enableAutoDeviceSwitchCheckBox.Checked;
            UpdateApplyButton();
        }

        private void LoadAudioDevices()
        {
            try
            {
                _audioDeviceComboBox.Items.Clear();
                _audioDeviceComboBox.Items.Add("Default System Device");

                // Try to get available audio devices from the service provider
                var audioCaptureService = _serviceProvider.GetService(typeof(TaskbarEqualizer.Core.Interfaces.IAudioCaptureService)) 
                    as TaskbarEqualizer.Core.Interfaces.IAudioCaptureService;

                if (audioCaptureService != null)
                {
                    var devices = audioCaptureService.GetAvailableDevices();
                    var deviceMap = new Dictionary<string, string>();

                    foreach (var device in devices)
                    {
                        try
                        {
                            var displayName = device.FriendlyName;
                            _audioDeviceComboBox.Items.Add(displayName);
                            deviceMap[displayName] = device.ID;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to add audio device to list: {DeviceId}", device.ID);
                        }
                    }

                    _audioDeviceComboBox.Tag = deviceMap;

                    // Select current device
                    if (string.IsNullOrEmpty(_settings.SelectedAudioDevice))
                    {
                        _audioDeviceComboBox.SelectedIndex = 0; // Default device
                    }
                    else
                    {
                        var selectedDevice = deviceMap.FirstOrDefault(kvp => kvp.Value == _settings.SelectedAudioDevice);
                        if (!selectedDevice.Equals(default(KeyValuePair<string, string>)))
                        {
                            _audioDeviceComboBox.SelectedItem = selectedDevice.Key;
                        }
                        else
                        {
                            _audioDeviceComboBox.SelectedIndex = 0; // Fallback to default
                        }
                    }
                }
                else
                {
                    _audioDeviceComboBox.SelectedIndex = 0; // Default device
                    _logger.LogWarning("AudioCaptureService not available, using default device selection");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load audio devices");
                _audioDeviceComboBox.Items.Clear();
                _audioDeviceComboBox.Items.Add("Default System Device");
                _audioDeviceComboBox.SelectedIndex = 0;
            }
        }

        private void OnDonationClick(object? sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://ko-fi.com/pathum",
                    UseShellExecute = true
                });
                _logger.LogInformation("Opened donation link");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open donation link");
                MessageBox.Show("Failed to open donation link. Please visit: https://ko-fi.com/pathum", 
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private async void OnApplyClick(object? sender, EventArgs e)
        {
            await ApplySettings();
        }

        private async void OnResetClick(object? sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to reset all settings to their default values?\nThis action cannot be undone.",
                "Reset Settings",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                try
                {
                    await _settingsManager.ResetToDefaultsAsync();
                    _settings = _settingsManager.Settings;
                    LoadSettings();
                    _logger.LogInformation("Settings reset to defaults");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to reset settings");
                    MessageBox.Show($"Failed to reset settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                // Don't use .Wait() here as it can cause deadlock
                // Instead, use synchronous saving
                try
                {
                    ApplySettingsSync();
                    _logger.LogInformation("Settings saved on dialog close");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save settings on dialog close");
                    MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    e.Cancel = true;
                }
            }
            else if (DialogResult == DialogResult.Cancel)
            {
                // Restore original settings
                _originalSettings.CopyTo(_settings);
                _logger.LogDebug("Settings restored to original values");
            }

            base.OnFormClosing(e);
        }

        private void ApplySettingsSync()
        {
            if (_isApplyingSettings)
            {
                _logger.LogDebug("Settings application already in progress, skipping");
                return;
            }

            try
            {
                _isApplyingSettings = true;

                _logger.LogDebug("Applying settings changes to live settings instance");

                // Copy the dialog settings to the actual settings instance
                _settings.CopyTo(_settingsManager.Settings);

                // Apply startup registry setting synchronously
                if (_settings.StartWithWindows != _originalSettings.StartWithWindows)
                {
                    ApplyStartWithWindowsSync(_settings.StartWithWindows);
                }

                // Save settings synchronously - using GetAwaiter().GetResult() to avoid deadlock issues
                var saveTask = _settingsManager.SaveAsync();
                if (!saveTask.Wait(5000)) // 5 second timeout
                {
                    throw new TimeoutException("Settings save operation timed out");
                }
                _originalSettings = _settings.Clone();
                _applyButton.Enabled = false;
                
                _logger.LogInformation("Settings applied successfully (sync)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply settings (sync)");
                throw;
            }
            finally
            {
                _isApplyingSettings = false;
            }
        }

        private void ApplyStartWithWindowsSync(bool enable)
        {
            try
            {
                // Use synchronous registry management
                ManageRegistryAutoStartSync(enable);
                _logger.LogInformation("Auto-start setting applied (sync): {Enabled}", enable);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply auto-start setting (sync)");
                // Don't throw here - this would cause the entire Apply operation to fail
                // Just log the error and continue with other settings
                MessageBox.Show($"Warning: Could not update startup settings: {ex.Message}", "Warning", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ManageRegistryAutoStartSync(bool enable)
        {
            const string keyName = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            const string valueName = "Graphlight";
            
            try
            {
                var exePath = GetExecutablePath();
                _logger.LogDebug("Executable path found: {ExePath}", exePath);
                
                using var key = Registry.CurrentUser.OpenSubKey(keyName, true);
                if (key != null)
                {
                    if (enable)
                    {
                        if (string.IsNullOrEmpty(exePath))
                        {
                            throw new InvalidOperationException("Could not determine executable path for auto-start");
                        }
                        
                        _logger.LogDebug("Setting registry auto-start value: {ValueName} = \"{ExePath}\"", valueName, exePath);
                        key.SetValue(valueName, $"\"{exePath}\"");
                    }
                    else
                    {
                        _logger.LogDebug("Removing registry auto-start value: {ValueName}", valueName);
                        key.DeleteValue(valueName, false);
                    }
                }
                else
                {
                    throw new InvalidOperationException("Could not open Windows startup registry key");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registry operation failed for auto-start - KeyName: {KeyName}, ValueName: {ValueName}, Enable: {Enable}", 
                    keyName, valueName, enable);
                throw;
            }
        }

        #endregion

        private void UpdateColorControlsEnabled()
        {
            var enabled = _useCustomColorsCheckBox.Checked;
            _primaryColorButton.Enabled = enabled;
            _secondaryColorButton.Enabled = enabled;
            _enableGradientCheckBox.Enabled = enabled;
            _gradientDirectionComboBox.Enabled = enabled && _enableGradientCheckBox.Checked;
        }

        private void UpdateApplyButton()
        {
            // Compare key properties instead of using Equals
            _applyButton.Enabled = HasSettingsChanged();
        }

        private bool HasSettingsChanged()
        {
            return _settings.StartWithWindows != _originalSettings.StartWithWindows ||
                   _settings.StartMinimized != _originalSettings.StartMinimized ||
                   _settings.ShowNotifications != _originalSettings.ShowNotifications ||
                   _settings.VisualizationStyle != _originalSettings.VisualizationStyle ||
                   _settings.RenderQuality != _originalSettings.RenderQuality ||
                   _settings.EnableAnimations != _originalSettings.EnableAnimations ||
                   _settings.EnableEffects != _originalSettings.EnableEffects ||
                   Math.Abs(_settings.Opacity - _originalSettings.Opacity) > 0.01f ||
                   _settings.UseCustomColors != _originalSettings.UseCustomColors ||
                   _settings.CustomPrimaryColor != _originalSettings.CustomPrimaryColor ||
                   _settings.CustomSecondaryColor != _originalSettings.CustomSecondaryColor ||
                   _settings.EnableGradient != _originalSettings.EnableGradient ||
                   _settings.GradientDirection != _originalSettings.GradientDirection ||
                   _settings.SelectedAudioDevice != _originalSettings.SelectedAudioDevice ||
                   _settings.EnableAutoDeviceSwitch != _originalSettings.EnableAutoDeviceSwitch ||
                   _settings.FrequencyBands != _originalSettings.FrequencyBands ||
                   Math.Abs(_settings.SmoothingFactor - _originalSettings.SmoothingFactor) > 0.01 ||
                   Math.Abs(_settings.GainFactor - _originalSettings.GainFactor) > 0.01 ||
                   Math.Abs(_settings.VolumeThreshold - _originalSettings.VolumeThreshold) > 0.01;
        }

        private async Task ApplySettings()
        {
            if (_isApplyingSettings)
            {
                _logger.LogDebug("Settings application already in progress, skipping async apply");
                return;
            }

            try
            {
                _isApplyingSettings = true;

                _logger.LogDebug("Applying settings changes to live settings instance (async)");

                // Copy the dialog settings to the actual settings instance
                _settings.CopyTo(_settingsManager.Settings);

                // Apply startup registry setting
                if (_settings.StartWithWindows != _originalSettings.StartWithWindows)
                {
                    await ApplyStartWithWindows(_settings.StartWithWindows);
                }

                await _settingsManager.SaveAsync();
                _originalSettings = _settings.Clone();
                _applyButton.Enabled = false;
                
                _logger.LogInformation("Settings applied successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply settings - Exception details: {ExceptionType}: {Message}", 
                    ex.GetType().Name, ex.Message);
                MessageBox.Show($"Failed to apply settings: {ex.Message}\n\nSee application logs for more details.", 
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isApplyingSettings = false;
            }
        }

        private async Task ApplyStartWithWindows(bool enable)
        {
            try
            {
                var autoStartManager = _serviceProvider.GetService<IAutoStartManager>();
                if (autoStartManager != null)
                {
                    if (enable)
                        await autoStartManager.EnableAutoStartAsync();
                    else
                        await autoStartManager.DisableAutoStartAsync();
                    
                    _logger.LogInformation("Auto-start setting applied: {Enabled}", enable);
                }
                else
                {
                    _logger.LogWarning("IAutoStartManager service not available, managing registry directly");
                    await ManageRegistryAutoStart(enable);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply auto-start setting");
                throw;
            }
        }

        private async Task ManageRegistryAutoStart(bool enable)
        {
            await Task.Run(() =>
            {
                const string keyName = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                const string valueName = "Graphlight";
                
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(keyName, true);
                    if (key != null)
                    {
                        if (enable)
                        {
                            var exePath = GetExecutablePath();
                            key.SetValue(valueName, $"\"{exePath}\"");
                        }
                        else
                        {
                            key.DeleteValue(valueName, false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to manage registry auto-start");
                    throw;
                }
            });
        }

        private static string GetExecutablePath()
        {
            // Try different methods to get the executable path
            
            // Method 1: Process main module (most reliable for .exe)
            try
            {
                using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                var mainModule = currentProcess.MainModule;
                if (mainModule?.FileName != null && System.IO.File.Exists(mainModule.FileName))
                {
                    return mainModule.FileName;
                }
            }
            catch { /* fallback to next method */ }

            // Method 2: Look for TaskbarEqualizer.exe in the same directory as this assembly
            try
            {
                var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var assemblyDir = System.IO.Path.GetDirectoryName(assemblyPath);
                if (!string.IsNullOrEmpty(assemblyDir))
                {
                    var exePath = System.IO.Path.Combine(assemblyDir, "TaskbarEqualizer.exe");
                    if (System.IO.File.Exists(exePath))
                    {
                        return exePath;
                    }
                }
            }
            catch { /* fallback to next method */ }

            // Method 3: Environment current directory + TaskbarEqualizer.exe
            try
            {
                var currentDir = Environment.CurrentDirectory;
                var exePath = System.IO.Path.Combine(currentDir, "TaskbarEqualizer.exe");
                if (System.IO.File.Exists(exePath))
                {
                    return exePath;
                }
            }
            catch { /* fallback to next method */ }

            // Method 4: Process name + exe extension
            try
            {
                var processName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
                if (!processName.EndsWith(".exe"))
                {
                    processName += ".exe";
                }
                var currentDir = Environment.CurrentDirectory;
                var exePath = System.IO.Path.Combine(currentDir, processName);
                if (System.IO.File.Exists(exePath))
                {
                    return exePath;
                }
            }
            catch { /* fallback to assembly location */ }

            // Fallback: If we can't find the exe, return a constructed path
            // This should not cause a "file not found" error in registry operations
            var fallbackPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (fallbackPath.EndsWith(".dll"))
            {
                var dir = System.IO.Path.GetDirectoryName(fallbackPath);
                fallbackPath = System.IO.Path.Combine(dir ?? "", "TaskbarEqualizer.exe");
            }
            return fallbackPath;
        }
    }
}