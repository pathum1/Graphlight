using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Configuration;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.SystemTray.Forms
{
    /// <summary>
    /// Settings window for customizing equalizer visualization options.
    /// Provides a modern Windows 11 styled interface for EQ configuration.
    /// </summary>
    public partial class SettingsWindow : Form
    {
        private readonly ILogger<SettingsWindow> _logger;
        private readonly ITaskbarOverlayManager _overlayManager;
        private ApplicationSettings _settings;
        private bool _isInitializing = true;
        
        // UI Controls
        private TabControl _tabControl = null!;
        private TabPage _visualTabPage = null!;
        private TabPage _audioTabPage = null!;
        private TabPage _behaviorTabPage = null!;
        
        // Visual settings controls
        private GroupBox _colorGroupBox = null!;
        private CheckBox _useGradientCheckBox = null!;
        private ComboBox _gradientDirectionComboBox = null!;
        private Button _primaryColorButton = null!;
        private Button _secondaryColorButton = null!;
        private Panel _primaryColorPanel = null!;
        private Panel _secondaryColorPanel = null!;
        private TrackBar _opacityTrackBar = null!;
        private Label _opacityValueLabel = null!;
        
        private GroupBox _styleGroupBox = null!;
        private RadioButton _solidBarsRadioButton = null!;
        private RadioButton _segmentedBarsRadioButton = null!;
        private NumericUpDown _segmentHeightNumeric = null!;
        private NumericUpDown _segmentGapNumeric = null!;
        private Label _segmentHeightLabel = null!;
        private Label _segmentGapLabel = null!;
        
        private GroupBox _effectsGroupBox = null!;
        private CheckBox _enableAnimationsCheckBox = null!;
        private CheckBox _enableEffectsCheckBox = null!;
        private TrackBar _animationSpeedTrackBar = null!;
        private Label _animationSpeedLabel = null!;
        
        // Audio settings controls
        private GroupBox _audioGroupBox = null!;
        private NumericUpDown _frequencyBandsNumeric = null!;
        private TrackBar _smoothingFactorTrackBar = null!;
        private TrackBar _gainFactorTrackBar = null!;
        private Label _smoothingValueLabel = null!;
        private Label _gainValueLabel = null!;
        
        // Buttons
        private Button _okButton = null!;
        private Button _cancelButton = null!;
        private Button _applyButton = null!;
        private Button _resetButton = null!;
        
        // Color dialog
        private ColorDialog _colorDialog = null!;

        public SettingsWindow(ILogger<SettingsWindow> logger, ITaskbarOverlayManager overlayManager, ApplicationSettings settings)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            
            InitializeComponent();
            LoadSettings();
            _isInitializing = false;
            
            _logger.LogDebug("SettingsWindow initialized");
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            
            // Main form properties
            Text = "TaskbarEqualizer Settings";
            Size = new Size(520, 600);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowIcon = false;
            ShowInTaskbar = false;
            BackColor = SystemColors.Control;
            Font = new Font("Segoe UI", 9F);
            
            CreateTabControl();
            CreateVisualTab();
            CreateAudioTab();
            CreateBehaviorTab();
            CreateButtons();
            CreateColorDialog();
            
            ResumeLayout();
        }

        private void CreateTabControl()
        {
            _tabControl = new TabControl
            {
                Location = new Point(12, 12),
                Size = new Size(484, 480),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            
            _visualTabPage = new TabPage("Visual") { UseVisualStyleBackColor = true };
            _audioTabPage = new TabPage("Audio") { UseVisualStyleBackColor = true };
            _behaviorTabPage = new TabPage("Behavior") { UseVisualStyleBackColor = true };
            
            _tabControl.Controls.AddRange(new TabPage[] { _visualTabPage, _audioTabPage, _behaviorTabPage });
            Controls.Add(_tabControl);
        }

        private void CreateVisualTab()
        {
            int yPos = 15;
            
            // Color settings group
            _colorGroupBox = new GroupBox
            {
                Text = "Color & Gradient",
                Location = new Point(15, yPos),
                Size = new Size(450, 180)
            };
            
            _useGradientCheckBox = new CheckBox
            {
                Text = "Enable gradient effects",
                Location = new Point(15, 25),
                Size = new Size(200, 20),
                Checked = _settings.EnableGradient
            };
            _useGradientCheckBox.CheckedChanged += OnGradientSettingChanged;
            
            _gradientDirectionComboBox = new ComboBox
            {
                Location = new Point(230, 23),
                Size = new Size(100, 21),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _gradientDirectionComboBox.Items.AddRange(new[] { "Vertical", "Horizontal", "Diagonal", "Radial" });
            _gradientDirectionComboBox.SelectedIndexChanged += OnGradientSettingChanged;
            
            var primaryColorLabel = new Label
            {
                Text = "Primary Color:",
                Location = new Point(15, 55),
                Size = new Size(80, 20)
            };
            
            _primaryColorButton = new Button
            {
                Location = new Point(100, 52),
                Size = new Size(80, 25),
                Text = "Choose..."
            };
            _primaryColorButton.Click += OnPrimaryColorClick;
            
            _primaryColorPanel = new Panel
            {
                Location = new Point(190, 52),
                Size = new Size(40, 25),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = _settings.CustomPrimaryColor
            };
            
            var secondaryColorLabel = new Label
            {
                Text = "Secondary Color:",
                Location = new Point(15, 85),
                Size = new Size(100, 20)
            };
            
            _secondaryColorButton = new Button
            {
                Location = new Point(115, 82),
                Size = new Size(80, 25),
                Text = "Choose..."
            };
            _secondaryColorButton.Click += OnSecondaryColorClick;
            
            _secondaryColorPanel = new Panel
            {
                Location = new Point(205, 82),
                Size = new Size(40, 25),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = _settings.CustomSecondaryColor
            };
            
            var opacityLabel = new Label
            {
                Text = "Opacity:",
                Location = new Point(15, 115),
                Size = new Size(60, 20)
            };
            
            _opacityTrackBar = new TrackBar
            {
                Location = new Point(75, 110),
                Size = new Size(200, 45),
                Minimum = 10,
                Maximum = 100,
                TickFrequency = 10,
                Value = (int)(_settings.Opacity * 100)
            };
            _opacityTrackBar.ValueChanged += OnOpacityChanged;
            
            _opacityValueLabel = new Label
            {
                Text = $"{_settings.Opacity:P0}",
                Location = new Point(285, 115),
                Size = new Size(40, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };
            
            _colorGroupBox.Controls.AddRange(new Control[] 
            {
                _useGradientCheckBox, _gradientDirectionComboBox,
                primaryColorLabel, _primaryColorButton, _primaryColorPanel,
                secondaryColorLabel, _secondaryColorButton, _secondaryColorPanel,
                opacityLabel, _opacityTrackBar, _opacityValueLabel
            });
            
            yPos += 190;
            
            // Style settings group
            _styleGroupBox = new GroupBox
            {
                Text = "Bar Style",
                Location = new Point(15, yPos),
                Size = new Size(450, 140)
            };
            
            _solidBarsRadioButton = new RadioButton
            {
                Text = "Solid rectangular bars",
                Location = new Point(15, 25),
                Size = new Size(200, 20),
                Checked = !_settings.CustomSettings.ContainsKey("UseSegmentedBars") || 
                         !(bool)_settings.CustomSettings.GetValueOrDefault("UseSegmentedBars", false)
            };
            _solidBarsRadioButton.CheckedChanged += OnBarStyleChanged;
            
            _segmentedBarsRadioButton = new RadioButton
            {
                Text = "Segmented VU meter style bars",
                Location = new Point(15, 50),
                Size = new Size(220, 20),
                Checked = _settings.CustomSettings.ContainsKey("UseSegmentedBars") && 
                         (bool)_settings.CustomSettings.GetValueOrDefault("UseSegmentedBars", false)
            };
            _segmentedBarsRadioButton.CheckedChanged += OnBarStyleChanged;
            
            _segmentHeightLabel = new Label
            {
                Text = "Segment Height:",
                Location = new Point(35, 80),
                Size = new Size(90, 20)
            };
            
            _segmentHeightNumeric = new NumericUpDown
            {
                Location = new Point(130, 78),
                Size = new Size(60, 21),
                Minimum = 2,
                Maximum = 10,
                Value = (decimal)_settings.CustomSettings.GetValueOrDefault("SegmentHeight", 5)
            };
            _segmentHeightNumeric.ValueChanged += OnSegmentSettingChanged;
            
            _segmentGapLabel = new Label
            {
                Text = "Segment Gap:",
                Location = new Point(200, 80),
                Size = new Size(80, 20)
            };
            
            _segmentGapNumeric = new NumericUpDown
            {
                Location = new Point(285, 78),
                Size = new Size(60, 21),
                Minimum = 0,
                Maximum = 5,
                Value = (decimal)_settings.CustomSettings.GetValueOrDefault("SegmentGap", 1)
            };
            _segmentGapNumeric.ValueChanged += OnSegmentSettingChanged;
            
            _styleGroupBox.Controls.AddRange(new Control[] 
            {
                _solidBarsRadioButton, _segmentedBarsRadioButton,
                _segmentHeightLabel, _segmentHeightNumeric,
                _segmentGapLabel, _segmentGapNumeric
            });
            
            yPos += 150;
            
            // Effects settings group
            _effectsGroupBox = new GroupBox
            {
                Text = "Animation & Effects",
                Location = new Point(15, yPos),
                Size = new Size(450, 100)
            };
            
            _enableAnimationsCheckBox = new CheckBox
            {
                Text = "Enable smooth animations",
                Location = new Point(15, 25),
                Size = new Size(180, 20),
                Checked = _settings.EnableAnimations
            };
            _enableAnimationsCheckBox.CheckedChanged += OnAnimationSettingChanged;
            
            _enableEffectsCheckBox = new CheckBox
            {
                Text = "Enable visual effects",
                Location = new Point(200, 25),
                Size = new Size(150, 20),
                Checked = _settings.EnableEffects
            };
            _enableEffectsCheckBox.CheckedChanged += OnAnimationSettingChanged;
            
            _animationSpeedLabel = new Label
            {
                Text = "Animation Speed:",
                Location = new Point(15, 55),
                Size = new Size(100, 20)
            };
            
            _animationSpeedTrackBar = new TrackBar
            {
                Location = new Point(120, 50),
                Size = new Size(150, 45),
                Minimum = 1,
                Maximum = 50,
                TickFrequency = 5,
                Value = (int)(_settings.AnimationSpeed * 10)
            };
            _animationSpeedTrackBar.ValueChanged += OnAnimationSpeedChanged;
            
            _effectsGroupBox.Controls.AddRange(new Control[] 
            {
                _enableAnimationsCheckBox, _enableEffectsCheckBox,
                _animationSpeedLabel, _animationSpeedTrackBar
            });
            
            UpdateSegmentControls();
            UpdateGradientControls();
            
            _visualTabPage.Controls.AddRange(new Control[] { _colorGroupBox, _styleGroupBox, _effectsGroupBox });
        }

        private void CreateAudioTab()
        {
            int yPos = 15;
            
            _audioGroupBox = new GroupBox
            {
                Text = "Audio Processing",
                Location = new Point(15, yPos),
                Size = new Size(450, 200)
            };
            
            var bandsLabel = new Label
            {
                Text = "Frequency Bands:",
                Location = new Point(15, 30),
                Size = new Size(100, 20)
            };
            
            _frequencyBandsNumeric = new NumericUpDown
            {
                Location = new Point(120, 28),
                Size = new Size(60, 21),
                Minimum = 4,
                Maximum = 64,
                Value = _settings.FrequencyBands
            };
            _frequencyBandsNumeric.ValueChanged += OnAudioSettingChanged;
            
            var smoothingLabel = new Label
            {
                Text = "Smoothing Factor:",
                Location = new Point(15, 65),
                Size = new Size(100, 20)
            };
            
            _smoothingFactorTrackBar = new TrackBar
            {
                Location = new Point(120, 60),
                Size = new Size(200, 45),
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                Value = (int)(_settings.SmoothingFactor * 100)
            };
            _smoothingFactorTrackBar.ValueChanged += OnSmoothingChanged;
            
            _smoothingValueLabel = new Label
            {
                Text = $"{_settings.SmoothingFactor:F2}",
                Location = new Point(330, 65),
                Size = new Size(40, 20)
            };
            
            var gainLabel = new Label
            {
                Text = "Gain Factor:",
                Location = new Point(15, 115),
                Size = new Size(80, 20)
            };
            
            _gainFactorTrackBar = new TrackBar
            {
                Location = new Point(120, 110),
                Size = new Size(200, 45),
                Minimum = 10,
                Maximum = 500,
                TickFrequency = 50,
                Value = (int)(_settings.GainFactor * 100)
            };
            _gainFactorTrackBar.ValueChanged += OnGainChanged;
            
            _gainValueLabel = new Label
            {
                Text = $"{_settings.GainFactor:F1}",
                Location = new Point(330, 115),
                Size = new Size(40, 20)
            };
            
            _audioGroupBox.Controls.AddRange(new Control[] 
            {
                bandsLabel, _frequencyBandsNumeric,
                smoothingLabel, _smoothingFactorTrackBar, _smoothingValueLabel,
                gainLabel, _gainFactorTrackBar, _gainValueLabel
            });
            
            _audioTabPage.Controls.Add(_audioGroupBox);
        }

        private void CreateBehaviorTab()
        {
            // Placeholder for future behavior settings
            var placeholderLabel = new Label
            {
                Text = "Behavior settings will be available in future updates.",
                Location = new Point(15, 30),
                Size = new Size(400, 20),
                ForeColor = SystemColors.GrayText
            };
            
            _behaviorTabPage.Controls.Add(placeholderLabel);
        }

        private void CreateButtons()
        {
            _okButton = new Button
            {
                Text = "OK",
                Location = new Point(261, 510),
                Size = new Size(75, 25),
                DialogResult = DialogResult.OK,
                UseVisualStyleBackColor = true
            };
            _okButton.Click += OnOkClick;
            
            _cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(342, 510),
                Size = new Size(75, 25),
                DialogResult = DialogResult.Cancel,
                UseVisualStyleBackColor = true
            };
            
            _applyButton = new Button
            {
                Text = "Apply",
                Location = new Point(423, 510),
                Size = new Size(75, 25),
                UseVisualStyleBackColor = true
            };
            _applyButton.Click += OnApplyClick;
            
            _resetButton = new Button
            {
                Text = "Reset",
                Location = new Point(12, 510),
                Size = new Size(75, 25),
                UseVisualStyleBackColor = true
            };
            _resetButton.Click += OnResetClick;
            
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
            
            Controls.AddRange(new Control[] { _okButton, _cancelButton, _applyButton, _resetButton });
        }

        private void CreateColorDialog()
        {
            _colorDialog = new ColorDialog
            {
                FullOpen = true,
                AnyColor = true
            };
        }

        private void LoadSettings()
        {
            _gradientDirectionComboBox.SelectedIndex = (int)_settings.GradientDirection;
            _primaryColorPanel.BackColor = _settings.CustomPrimaryColor;
            _secondaryColorPanel.BackColor = _settings.CustomSecondaryColor;
        }

        private void OnGradientSettingChanged(object? sender, EventArgs e)
        {
            if (_isInitializing) return;
            UpdateGradientControls();
            ApplyChanges();
        }

        private void OnBarStyleChanged(object? sender, EventArgs e)
        {
            if (_isInitializing) return;
            UpdateSegmentControls();
            ApplyChanges();
        }

        private void OnSegmentSettingChanged(object? sender, EventArgs e)
        {
            if (_isInitializing) return;
            ApplyChanges();
        }

        private void OnAnimationSettingChanged(object? sender, EventArgs e)
        {
            if (_isInitializing) return;
            ApplyChanges();
        }

        private void OnAnimationSpeedChanged(object? sender, EventArgs e)
        {
            if (_isInitializing) return;
            ApplyChanges();
        }

        private void OnAudioSettingChanged(object? sender, EventArgs e)
        {
            if (_isInitializing) return;
            ApplyChanges();
        }

        private void OnOpacityChanged(object? sender, EventArgs e)
        {
            if (_isInitializing) return;
            _opacityValueLabel.Text = $"{_opacityTrackBar.Value}%";
            ApplyChanges();
        }

        private void OnSmoothingChanged(object? sender, EventArgs e)
        {
            if (_isInitializing) return;
            _smoothingValueLabel.Text = $"{_smoothingFactorTrackBar.Value / 100.0:F2}";
            ApplyChanges();
        }

        private void OnGainChanged(object? sender, EventArgs e)
        {
            if (_isInitializing) return;
            _gainValueLabel.Text = $"{_gainFactorTrackBar.Value / 100.0:F1}";
            ApplyChanges();
        }

        private void OnPrimaryColorClick(object? sender, EventArgs e)
        {
            _colorDialog.Color = _primaryColorPanel.BackColor;
            if (_colorDialog.ShowDialog() == DialogResult.OK)
            {
                _primaryColorPanel.BackColor = _colorDialog.Color;
                ApplyChanges();
            }
        }

        private void OnSecondaryColorClick(object? sender, EventArgs e)
        {
            _colorDialog.Color = _secondaryColorPanel.BackColor;
            if (_colorDialog.ShowDialog() == DialogResult.OK)
            {
                _secondaryColorPanel.BackColor = _colorDialog.Color;
                ApplyChanges();
            }
        }

        private void UpdateGradientControls()
        {
            _gradientDirectionComboBox.Enabled = _useGradientCheckBox.Checked;
            _secondaryColorButton.Enabled = _useGradientCheckBox.Checked;
            _secondaryColorPanel.Enabled = _useGradientCheckBox.Checked;
        }

        private void UpdateSegmentControls()
        {
            bool isSegmented = _segmentedBarsRadioButton.Checked;
            _segmentHeightLabel.Enabled = isSegmented;
            _segmentHeightNumeric.Enabled = isSegmented;
            _segmentGapLabel.Enabled = isSegmented;
            _segmentGapNumeric.Enabled = isSegmented;
        }

        private void ApplyChanges()
        {
            UpdateSettingsFromUI();
            
            try
            {
                // Apply changes immediately for preview
                var overlayConfig = CreateOverlayConfigurationFromSettings();
                _overlayManager.UpdateConfigurationAsync(overlayConfig);
                
                _logger.LogDebug("Settings applied successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply settings");
                MessageBox.Show($"Failed to apply settings: {ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateSettingsFromUI()
        {
            _settings.EnableGradient = _useGradientCheckBox.Checked;
            _settings.GradientDirection = (Configuration.GradientDirection)_gradientDirectionComboBox.SelectedIndex;
            _settings.CustomPrimaryColor = _primaryColorPanel.BackColor;
            _settings.CustomSecondaryColor = _secondaryColorPanel.BackColor;
            _settings.UseCustomColors = true;
            _settings.Opacity = _opacityTrackBar.Value / 100.0f;
            
            // Bar style settings
            _settings.CustomSettings["UseSegmentedBars"] = _segmentedBarsRadioButton.Checked;
            _settings.CustomSettings["SegmentHeight"] = (int)_segmentHeightNumeric.Value;
            _settings.CustomSettings["SegmentGap"] = (int)_segmentGapNumeric.Value;
            
            // Animation settings
            _settings.EnableAnimations = _enableAnimationsCheckBox.Checked;
            _settings.EnableEffects = _enableEffectsCheckBox.Checked;
            _settings.AnimationSpeed = _animationSpeedTrackBar.Value / 10.0;
            
            // Audio settings
            _settings.FrequencyBands = (int)_frequencyBandsNumeric.Value;
            _settings.SmoothingFactor = _smoothingFactorTrackBar.Value / 100.0;
            _settings.GainFactor = _gainFactorTrackBar.Value / 100.0;
        }

        private OverlayConfiguration CreateOverlayConfigurationFromSettings()
        {
            var config = _overlayManager.Configuration;
            
            config.Opacity = _settings.Opacity;
            config.RenderConfiguration.ColorScheme.PrimaryColor = _settings.CustomPrimaryColor;
            config.RenderConfiguration.ColorScheme.SecondaryColor = _settings.CustomSecondaryColor;
            config.RenderConfiguration.ColorScheme.UseGradient = _settings.EnableGradient;
            config.RenderConfiguration.ColorScheme.GradientDirection = (Interfaces.GradientDirection)_settings.GradientDirection;
            config.RenderConfiguration.EnableEffects = _settings.EnableEffects;
            config.RenderConfiguration.Animation.SmoothingFactor = _settings.SmoothingFactor;
            
            // Copy custom settings
            config.CustomSettings.Clear();
            foreach (var kvp in _settings.CustomSettings)
            {
                config.CustomSettings[kvp.Key] = kvp.Value;
            }
            
            return config;
        }

        private void OnOkClick(object? sender, EventArgs e)
        {
            UpdateSettingsFromUI();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnApplyClick(object? sender, EventArgs e)
        {
            ApplyChanges();
        }

        private void OnResetClick(object? sender, EventArgs e)
        {
            if (MessageBox.Show("Reset all settings to defaults?", "Confirm Reset",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _settings = ApplicationSettings.CreateDefault();
                LoadSettings();
                ApplyChanges();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _colorDialog?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

}