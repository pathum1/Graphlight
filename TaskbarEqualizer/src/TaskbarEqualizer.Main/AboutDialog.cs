using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace TaskbarEqualizer.Main
{
    /// <summary>
    /// About dialog for Graphlight (TaskbarEqualizer) with modern styling.
    /// Displays application information, version, and donation link.
    /// </summary>
    public partial class AboutDialog : Form
    {
        private readonly ILogger<AboutDialog> _logger;
        
        // UI Controls
        private TableLayoutPanel _mainLayout = null!;
        private Label _appNameLabel = null!;
        private Label _versionLabel = null!;
        private Label _descriptionLabel = null!;
        private Button _donationButton = null!;
        private Button _closeButton = null!;
        private PictureBox _iconPictureBox = null!;

        public AboutDialog(ILogger<AboutDialog>? logger = null)
        {
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AboutDialog>.Instance;
            
            InitializeComponent();
            ApplyModernStyling();
            LoadApplicationInfo();
            
            _logger.LogInformation("About dialog initialized");
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            // Form properties
            Text = "About Graphlight";
            Size = new Size(400, 300);
            MinimumSize = new Size(350, 250);
            MaximizeBox = false;
            MinimizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Icon = SystemIcons.Application;

            // Main layout
            _mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 7,
                ColumnCount = 1,
                Padding = new Padding(20)
            };

            // Configure rows
            _mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F)); // Icon
            _mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F)); // App name
            _mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F)); // Version
            _mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F)); // Description
            _mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F)); // Donation button
            _mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Spacer
            _mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F)); // Close button

            // Application Icon
            _iconPictureBox = new PictureBox
            {
                Image = SystemIcons.Application.ToBitmap(),
                SizeMode = PictureBoxSizeMode.CenterImage,
                Size = new Size(48, 48),
                Anchor = AnchorStyles.None
            };
            _mainLayout.Controls.Add(_iconPictureBox, 0, 0);

            // App Name
            _appNameLabel = new Label
            {
                Text = "Graphlight",
                Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(0, 120, 215) // Windows 11 accent blue
            };
            _mainLayout.Controls.Add(_appNameLabel, 0, 1);

            // Version
            _versionLabel = new Label
            {
                Text = "Version 1.0.0",
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 9F)
            };
            _mainLayout.Controls.Add(_versionLabel, 0, 2);

            // Description
            _descriptionLabel = new Label
            {
                Text = "Professional audio visualizer for Windows taskbar\nReal-time spectrum analysis with modern design",
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 9F)
            };
            _mainLayout.Controls.Add(_descriptionLabel, 0, 3);

            // Donation Button
            _donationButton = new Button
            {
                Text = "☕ Support Development",
                Size = new Size(180, 30),
                Anchor = AnchorStyles.None,
                BackColor = Color.FromArgb(255, 95, 135), // Ko-Fi brand color
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(Font.FontFamily, 9F, FontStyle.Bold)
            };
            _donationButton.FlatAppearance.BorderSize = 0;
            _donationButton.Click += OnDonationClick;
            _mainLayout.Controls.Add(_donationButton, 0, 4);

            // Close Button
            _closeButton = new Button
            {
                Text = "Close",
                Size = new Size(80, 25),
                Anchor = AnchorStyles.None,
                DialogResult = DialogResult.OK
            };
            _mainLayout.Controls.Add(_closeButton, 0, 6);

            Controls.Add(_mainLayout);
            ResumeLayout(false);
        }

        private void ApplyModernStyling()
        {
            // Enable visual styles
            Application.EnableVisualStyles();
            
            // Set modern colors
            BackColor = SystemColors.Window;
            ForeColor = SystemColors.WindowText;

            // Add subtle border effect to donation button
            _donationButton.FlatAppearance.BorderColor = Color.FromArgb(220, 75, 115);
        }

        private void LoadApplicationInfo()
        {
            try
            {
                // Get version from assembly
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);

                // Update version label
                _versionLabel.Text = $"Version {version?.ToString(3) ?? "1.0.0"}";

                // Update description with file description if available
                if (!string.IsNullOrEmpty(fileVersionInfo.FileDescription))
                {
                    _descriptionLabel.Text = fileVersionInfo.FileDescription;
                }

                // Update app name if available from assembly
                if (!string.IsNullOrEmpty(fileVersionInfo.ProductName))
                {
                    _appNameLabel.Text = fileVersionInfo.ProductName;
                    Text = $"About {fileVersionInfo.ProductName}";
                }

                _logger.LogDebug("Application info loaded: Version {Version}", version);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load complete application info, using defaults");
            }
        }

        #region Event Handlers

        private void OnDonationClick(object? sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://ko-fi.com/pathum",
                    UseShellExecute = true
                });
                _logger.LogInformation("Opened donation link from About dialog");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open donation link from About dialog");
                MessageBox.Show(
                    "Failed to open donation link. Please visit: https://ko-fi.com/pathum", 
                    "Information", 
                    MessageBoxButtons.OK, 
                    MessageBoxIcon.Information);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
            base.OnKeyDown(e);
        }

        #endregion

        /// <summary>
        /// Shows the About dialog modally.
        /// </summary>
        /// <param name="owner">The owner window.</param>
        /// <returns>DialogResult from the dialog.</returns>
        public static new DialogResult Show(IWin32Window? owner = null)
        {
            using var dialog = new AboutDialog();
            return owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        }

        /// <summary>
        /// Shows the About dialog modally with logging.
        /// </summary>
        /// <param name="logger">Logger instance for diagnostic information.</param>
        /// <param name="owner">The owner window.</param>
        /// <returns>DialogResult from the dialog.</returns>
        public static DialogResult Show(ILogger<AboutDialog>? logger, IWin32Window? owner = null)
        {
            using var dialog = new AboutDialog(logger);
            return owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        }
    }
}