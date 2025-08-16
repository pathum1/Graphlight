using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Core.Interfaces;
using TaskbarEqualizer.Configuration;
using TaskbarEqualizer.Configuration.Interfaces;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.Main
{
    /// <summary>
    /// Main spectrum analyzer window that displays the audio visualization.
    /// </summary>
    public partial class SpectrumAnalyzerWindow : Form
    {
        private readonly ILogger<SpectrumAnalyzerWindow> _logger;
        private readonly ISettingsManager? _settingsManager;
        private SpectrumDataEventArgs? _currentSpectrum;
        private Timer? _refreshTimer;
        private float[] _smoothedSpectrum = new float[16]; // Default to 16 bands from ApplicationSettings
        private float _smoothingFactor = 0.8f; // Default from ApplicationSettings
        private double _gainFactor = 1.0; // Default from ApplicationSettings
        private ApplicationSettings? _settings;

        public SpectrumAnalyzerWindow(ILogger<SpectrumAnalyzerWindow> logger, ISettingsManager? settingsManager = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settingsManager = settingsManager;
            InitializeComponent();
            SetupWindow();
            StartRefreshTimer();
        }

        /// <summary>
        /// Initializes the spectrum window with application settings.
        /// </summary>
        /// <param name="settings">The application settings to use.</param>
        public void InitializeWithSettings(ApplicationSettings settings)
        {
            UpdateSettings(settings);
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            
            // Form settings
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 400);
            Text = "TaskbarEqualizer - Professional Audio Visualizer";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(400, 200);
            BackColor = Color.Black;
            
            
            // Enable double buffering for smooth animation
            SetStyle(ControlStyles.AllPaintingInWmPaint | 
                    ControlStyles.UserPaint | 
                    ControlStyles.DoubleBuffer | 
                    ControlStyles.ResizeRedraw, true);

            ResumeLayout(false);
        }

        private void SetupWindow()
        {
            // Set icon if available
            try
            {
                Icon = LoadApplicationIcon();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not set window icon");
                Icon = SystemIcons.Application; // Fallback
            }

            // Handle window closing
            FormClosing += SpectrumAnalyzerWindow_FormClosing;
            
            // Handle resize
            Resize += SpectrumAnalyzerWindow_Resize;
            
            // Handle location changes for position persistence
            LocationChanged += SpectrumAnalyzerWindow_LocationChanged;
            
            _logger.LogInformation("Spectrum analyzer window initialized");
        }

        private void SpectrumAnalyzerWindow_Resize(object? sender, EventArgs e)
        {
            // Hide from taskbar when minimized
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                ShowInTaskbar = false;
            }
            else if (WindowState == FormWindowState.Normal && _settings != null)
            {
                // Save window size when it changes
                SaveWindowLocation();
            }
        }

        private void SpectrumAnalyzerWindow_FormClosing(object? sender, FormClosingEventArgs e)
        {
            // Save window location before closing/hiding
            SaveWindowLocation();
            
            // Minimize to tray instead of closing
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                _logger.LogInformation("Window minimized to system tray");
            }
        }

        private void SpectrumAnalyzerWindow_LocationChanged(object? sender, EventArgs e)
        {
            // Save window location when it changes
            if (WindowState == FormWindowState.Normal && _settings != null)
            {
                SaveWindowLocation();
            }
        }

        private void SaveWindowLocation()
        {
            if (_settings != null && WindowState == FormWindowState.Normal)
            {
                var rememberPosition = GetRememberPositionSetting();
                if (rememberPosition)
                {
                    _settings.WindowLocation = Location;
                    _settings.WindowSize = Size;
                    _logger.LogDebug("Saved window position: {Location} and size: {Size}", Location, Size);
                    
                    // Save settings to disk asynchronously
                    if (_settingsManager != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _settingsManager.SaveAsync();
                                _logger.LogDebug("Window position and size saved to disk");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to save window position to disk");
                            }
                        });
                    }
                    else
                    {
                        _logger.LogDebug("Settings manager not available, position saved in memory only");
                    }
                }
            }
        }

        private bool GetRememberPositionSetting()
        {
            // Use the strongly-typed RememberPosition property
            return _settings?.RememberPosition ?? false;
        }

        private bool IsLocationOnScreen(Point location)
        {
            // Check if the location is within any of the available screens
            foreach (var screen in Screen.AllScreens)
            {
                // Allow some tolerance for window borders
                var bounds = screen.WorkingArea;
                bounds.Inflate(-50, -50); // Give 50px margin
                
                if (bounds.Contains(location))
                {
                    return true;
                }
            }
            
            return false;
        }


        public void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            BringToFront();
            Focus();
            _logger.LogInformation("Spectrum analyzer window shown");
        }

        public void HideWindow()
        {
            Hide();
            ShowInTaskbar = false;
            _logger.LogInformation("Spectrum analyzer window hidden");
        }

        private void StartRefreshTimer()
        {
            _refreshTimer = new Timer
            {
                Interval = (int)(_settings?.UpdateInterval ?? 16.67) // Use settings or default ~60 FPS
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();
        }

        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            Invalidate(); // Trigger repaint
        }

        public void UpdateSpectrum(SpectrumDataEventArgs spectrumData)
        {
            _currentSpectrum = spectrumData;
            
            // Apply smoothing to prevent jittery animations
            if (spectrumData.Spectrum != null)
            {
                var targetLength = Math.Min(_smoothedSpectrum.Length, spectrumData.Spectrum.Length);
                for (int i = 0; i < targetLength; i++)
                {
                    _smoothedSpectrum[i] = _smoothedSpectrum[i] * _smoothingFactor + 
                                         (float)spectrumData.Spectrum[i] * (1f - _smoothingFactor);
                }
            }
        }


        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.CompositingQuality = CompositingQuality.HighQuality;
            
            // Clear background
            g.Clear(Color.Black);
            
            DrawSpectrum(g);
            DrawInfo(g);
        }

        private void DrawSpectrum(Graphics g)
        {
            if (_currentSpectrum?.Spectrum == null || _currentSpectrum.Spectrum.Length == 0)
            {
                DrawNoAudioMessage(g);
                return;
            }

            var clientRect = ClientRectangle;
            var barCount = _smoothedSpectrum.Length;
            var barWidth = (float)clientRect.Width / barCount;
            var maxHeight = clientRect.Height - 60; // Leave space for info text

            // Determine colors based on settings
            Color primaryColor, secondaryColor;
            if (_settings?.UseCustomColors == true)
            {
                primaryColor = _settings.CustomPrimaryColor;
                secondaryColor = _settings.CustomSecondaryColor;
            }
            else
            {
                // Default colors (green to red)
                primaryColor = Color.FromArgb(0, 255, 100);    // Green at bottom
                secondaryColor = Color.FromArgb(255, 100, 0);  // Red at top
            }

            // Check visualization style
            var visualizationStyle = _settings?.VisualizationStyle ?? EqualizerStyle.Bars;
            
            // Debug logging to verify colors and styles are being applied - FORCE THIS TO INFO LEVEL
            _logger.LogInformation("DRAWING SPECTRUM: Style={Style}, UseCustomColors={UseCustom}, Primary={Primary}, Secondary={Secondary}",
                visualizationStyle, _settings?.UseCustomColors, primaryColor, secondaryColor);

            switch (visualizationStyle)
            {
                case EqualizerStyle.Bars:
                    DrawBars(g, clientRect, barCount, barWidth, maxHeight, primaryColor, secondaryColor);
                    break;
                case EqualizerStyle.Dots:
                    DrawDots(g, clientRect, barCount, barWidth, maxHeight, primaryColor, secondaryColor);
                    break;
                case EqualizerStyle.Waveform:
                    DrawWaveform(g, clientRect, maxHeight, primaryColor, secondaryColor);
                    break;
                case EqualizerStyle.Spectrum:
                    DrawSpectrumStyle(g, clientRect, barCount, barWidth, maxHeight, primaryColor, secondaryColor);
                    break;
                case EqualizerStyle.Lines:
                    DrawLines(g, clientRect, barCount, barWidth, maxHeight, primaryColor, secondaryColor);
                    break;
                default:
                    DrawBars(g, clientRect, barCount, barWidth, maxHeight, primaryColor, secondaryColor);
                    break;
            }
        }

        private void DrawBars(Graphics g, Rectangle clientRect, int barCount, float barWidth, int maxHeight, Color primaryColor, Color secondaryColor)
        {
            // Create brush based on gradient settings
            Brush brush;
            if (_settings?.EnableGradient == true)
            {
                var gradientMode = _settings.GradientDirection switch
                {
                    GradientDirection.Horizontal => LinearGradientMode.Horizontal,
                    GradientDirection.Diagonal => LinearGradientMode.ForwardDiagonal,
                    _ => LinearGradientMode.Vertical
                };
                
                brush = new LinearGradientBrush(
                    new Rectangle(0, 0, clientRect.Width, maxHeight),
                    primaryColor, secondaryColor, gradientMode);
            }
            else
            {
                brush = new SolidBrush(primaryColor);
            }

            using (brush)
            {
                // Draw frequency bars
                for (int i = 0; i < barCount; i++)
                {
                    var level = Math.Max(0, Math.Min(1, _smoothedSpectrum[i] * (float)_gainFactor));
                    var barHeight = (int)(level * maxHeight);
                    
                    if (barHeight > 0)
                    {
                        var x = i * barWidth;
                        var y = clientRect.Height - 40 - barHeight; // 40px from bottom for info
                        var width = Math.Max(1, barWidth - 2); // 2px gap between bars
                        
                        g.FillRectangle(brush, x, y, width, barHeight);
                    }
                }
            }

            // Draw peak indicators
            using var peakPen = new Pen(Color.White, 1);
            for (int i = 0; i < barCount; i++)
            {
                var level = Math.Max(0, Math.Min(1, _smoothedSpectrum[i] * (float)_gainFactor));
                if (level > 0.1f) // Only show peaks above threshold
                {
                    var peakHeight = (int)(level * maxHeight);
                    var x = i * barWidth;
                    var y = clientRect.Height - 40 - peakHeight;
                    var width = Math.Max(1, barWidth - 2);
                    
                    g.DrawLine(peakPen, x, y, x + width, y);
                }
            }
        }

        private void DrawDots(Graphics g, Rectangle clientRect, int barCount, float barWidth, int maxHeight, Color primaryColor, Color secondaryColor)
        {
            var dotSize = Math.Max(2, Math.Min(8, barWidth * 0.6f)); // Dot size based on available width
            
            for (int i = 0; i < barCount; i++)
            {
                var level = Math.Max(0, Math.Min(1, _smoothedSpectrum[i] * (float)_gainFactor));
                var numDots = (int)(level * (maxHeight / (dotSize + 2))); // Number of dots based on level
                
                var x = i * barWidth + (barWidth - dotSize) / 2; // Center dot in bar space
                
                for (int d = 0; d < numDots; d++)
                {
                    var y = clientRect.Height - 40 - (d * (dotSize + 2)) - dotSize;
                    
                    // Color interpolation for gradient effect
                    Color dotColor;
                    if (_settings?.EnableGradient == true)
                    {
                        var ratio = (float)d / Math.Max(1, numDots - 1);
                        dotColor = InterpolateColor(primaryColor, secondaryColor, ratio);
                    }
                    else
                    {
                        dotColor = primaryColor;
                    }
                    
                    using var brush = new SolidBrush(dotColor);
                    g.FillEllipse(brush, x, y, dotSize, dotSize);
                }
            }
        }

        private void DrawWaveform(Graphics g, Rectangle clientRect, int maxHeight, Color primaryColor, Color secondaryColor)
        {
            if (_smoothedSpectrum.Length < 2) return;

            var points = new List<PointF>();
            var barCount = _smoothedSpectrum.Length;
            var stepX = (float)clientRect.Width / (barCount - 1);

            // Create waveform points
            for (int i = 0; i < barCount; i++)
            {
                var level = Math.Max(0, Math.Min(1, _smoothedSpectrum[i] * (float)_gainFactor));
                var x = i * stepX;
                var y = clientRect.Height - 40 - (level * maxHeight); // 40px from bottom for info
                points.Add(new PointF(x, y));
            }

            // Draw the waveform
            if (points.Count >= 2)
            {
                var penWidth = Math.Max(1, clientRect.Width / 200); // Adaptive pen width
                using var pen = new Pen(primaryColor, penWidth);
                
                // Smooth curve if we have enough points
                if (points.Count >= 3)
                {
                    g.DrawCurve(pen, points.ToArray());
                }
                else
                {
                    g.DrawLines(pen, points.ToArray());
                }

                // Optional: fill under the curve
                if (_settings?.EnableEffects == true)
                {
                    // Add baseline points for filling
                    var fillPoints = new List<PointF>(points);
                    fillPoints.Add(new PointF(clientRect.Width, clientRect.Height - 40));
                    fillPoints.Add(new PointF(0, clientRect.Height - 40));

                    using var fillBrush = new SolidBrush(Color.FromArgb(64, primaryColor)); // Semi-transparent
                    g.FillPolygon(fillBrush, fillPoints.ToArray());
                }
            }
        }

        private static Color InterpolateColor(Color start, Color end, float ratio)
        {
            ratio = Math.Max(0, Math.Min(1, ratio));
            
            var r = (int)(start.R + (end.R - start.R) * ratio);
            var g = (int)(start.G + (end.G - start.G) * ratio);
            var b = (int)(start.B + (end.B - start.B) * ratio);
            var a = (int)(start.A + (end.A - start.A) * ratio);
            
            return Color.FromArgb(a, r, g, b);
        }

        private void DrawNoAudioMessage(Graphics g)
        {
            var message = "No Audio Input Detected";
            var font = new Font("Segoe UI", 24, FontStyle.Bold);
            var brush = new SolidBrush(Color.Gray);
            var size = g.MeasureString(message, font);
            var x = (ClientSize.Width - size.Width) / 2;
            var y = (ClientSize.Height - size.Height) / 2;
            
            g.DrawString(message, font, brush, x, y);
            
            font.Dispose();
            brush.Dispose();
        }

        private void DrawInfo(Graphics g)
        {
            if (_currentSpectrum == null)
                return;

            var infoText = $"Peak: {_currentSpectrum.PeakValue:F3} | RMS: {_currentSpectrum.RmsLevel:F3} | " +
                          $"Bands: {_currentSpectrum.Spectrum?.Length ?? 0} | " +
                          $"FPS: {1000 / Math.Max(1, _refreshTimer?.Interval ?? 33):F0}";

            using var font = new Font("Segoe UI", 10);
            using var brush = new SolidBrush(Color.LightGray);
            
            var textSize = g.MeasureString(infoText, font);
            var x = 10;
            var y = ClientSize.Height - 30;
            
            // Draw background for text
            using var bgBrush = new SolidBrush(Color.FromArgb(128, 0, 0, 0));
            g.FillRectangle(bgBrush, x - 5, y - 5, textSize.Width + 10, textSize.Height + 10);
            
            g.DrawString(infoText, font, brush, x, y);
        }

        private void DrawSpectrumStyle(Graphics g, Rectangle clientRect, int barCount, float barWidth, int maxHeight, Color primaryColor, Color secondaryColor)
        {
            // Spectrum style: Similar to bars but with more continuous fill and glow effects
            Brush brush;
            if (_settings?.EnableGradient == true)
            {
                var gradientMode = _settings.GradientDirection switch
                {
                    GradientDirection.Horizontal => LinearGradientMode.Horizontal,
                    GradientDirection.Diagonal => LinearGradientMode.ForwardDiagonal,
                    _ => LinearGradientMode.Vertical
                };
                
                brush = new LinearGradientBrush(
                    new Rectangle(0, 0, clientRect.Width, maxHeight),
                    primaryColor, secondaryColor, gradientMode);
            }
            else
            {
                brush = new SolidBrush(primaryColor);
            }

            using (brush)
            {
                // Draw spectrum bars with no gaps for continuous look
                for (int i = 0; i < barCount; i++)
                {
                    var level = Math.Max(0, Math.Min(1, _smoothedSpectrum[i] * (float)_gainFactor));
                    var barHeight = (int)(level * maxHeight);
                    
                    if (barHeight > 0)
                    {
                        var x = i * barWidth;
                        var y = clientRect.Height - 40 - barHeight; // 40px from bottom for info
                        var width = barWidth; // No gaps between bars
                        
                        g.FillRectangle(brush, x, y, width, barHeight);
                        
                        // Add glow effect if effects are enabled
                        if (_settings?.EnableEffects == true)
                        {
                            using var glowBrush = new SolidBrush(Color.FromArgb(64, primaryColor));
                            g.FillRectangle(glowBrush, x - 1, y - 1, width + 2, barHeight + 2);
                        }
                    }
                }
            }
        }

        private void DrawLines(Graphics g, Rectangle clientRect, int barCount, float barWidth, int maxHeight, Color primaryColor, Color secondaryColor)
        {
            // Lines style: Thin vertical lines instead of bars
            var penWidth = Math.Max(1, barWidth * 0.3f); // Thin lines
            
            for (int i = 0; i < barCount; i++)
            {
                var level = Math.Max(0, Math.Min(1, _smoothedSpectrum[i] * (float)_gainFactor));
                var lineHeight = (int)(level * maxHeight);
                
                if (lineHeight > 0)
                {
                    Color lineColor;
                    if (_settings?.EnableGradient == true)
                    {
                        var ratio = (float)level;
                        lineColor = InterpolateColor(primaryColor, secondaryColor, ratio);
                    }
                    else
                    {
                        lineColor = primaryColor;
                    }
                    
                    using var pen = new Pen(lineColor, penWidth);
                    var x = i * barWidth + barWidth / 2; // Center the line
                    var y1 = clientRect.Height - 40;
                    var y2 = y1 - lineHeight;
                    
                    g.DrawLine(pen, x, y1, x, y2);
                    
                    // Add dot at the top of each line for visual appeal
                    if (_settings?.EnableEffects == true && lineHeight > 5)
                    {
                        using var dotBrush = new SolidBrush(lineColor);
                        g.FillEllipse(dotBrush, x - 2, y2 - 2, 4, 4);
                    }
                }
            }
        }

        /// <summary>
        /// Updates the spectrum window with new settings.
        /// </summary>
        /// <param name="settings">The updated application settings.</param>
        public void UpdateSettings(ApplicationSettings settings)
        {
            try
            {
                _settings = settings ?? throw new ArgumentNullException(nameof(settings));
                
                _logger.LogInformation("Updating spectrum window settings");

                // Update frequency bands - resize smoothed spectrum array if needed
                if (_smoothedSpectrum.Length != settings.FrequencyBands)
                {
                    _logger.LogInformation("Resizing spectrum array from {OldSize} to {NewSize}", 
                        _smoothedSpectrum.Length, settings.FrequencyBands);
                    
                    var newSpectrum = new float[settings.FrequencyBands];
                    
                    // Copy existing data if possible
                    var copyLength = Math.Min(_smoothedSpectrum.Length, newSpectrum.Length);
                    Array.Copy(_smoothedSpectrum, newSpectrum, copyLength);
                    
                    _smoothedSpectrum = newSpectrum;
                }

                // Update smoothing factor
                _smoothingFactor = (float)settings.SmoothingFactor;
                _logger.LogDebug("Updated smoothing factor to: {SmoothingFactor}", _smoothingFactor);

                // Update gain factor
                _gainFactor = settings.GainFactor;
                _logger.LogDebug("Updated gain factor to: {GainFactor}", _gainFactor);

                // Log color and style settings
                _logger.LogInformation("Color/Style settings: UseCustomColors={UseCustomColors}, " +
                    "PrimaryColor={PrimaryColor}, SecondaryColor={SecondaryColor}, VisualizationStyle={VisualizationStyle}",
                    settings.UseCustomColors, settings.CustomPrimaryColor, settings.CustomSecondaryColor, settings.VisualizationStyle);

                // Update refresh timer interval
                if (_refreshTimer != null)
                {
                    var newInterval = (int)settings.UpdateInterval;
                    if (_refreshTimer.Interval != newInterval)
                    {
                        _refreshTimer.Stop();
                        _refreshTimer.Interval = newInterval;
                        _refreshTimer.Start();
                        _logger.LogDebug("Updated refresh interval to: {Interval}ms", newInterval);
                    }
                }

                // Restore window position and size if remember position is enabled and valid values are saved
                var rememberPosition = GetRememberPositionSetting();
                if (rememberPosition)
                {
                    // Restore window location
                    if (settings.WindowLocation != Point.Empty)
                    {
                        var savedLocation = settings.WindowLocation;
                        
                        // Validate that the saved location is within screen bounds
                        if (IsLocationOnScreen(savedLocation))
                        {
                            Location = savedLocation;
                            _logger.LogDebug("Restored window location to: {Location}", savedLocation);
                        }
                        else
                        {
                            _logger.LogWarning("Saved window location {Location} is outside screen bounds, using default", savedLocation);
                        }
                    }
                    
                    // Restore window size
                    if (settings.WindowSize != Size.Empty && settings.WindowSize.Width >= MinimumSize.Width && settings.WindowSize.Height >= MinimumSize.Height)
                    {
                        Size = settings.WindowSize;
                        _logger.LogDebug("Restored window size to: {Size}", settings.WindowSize);
                    }
                }

                // Force a redraw with new settings
                if (InvokeRequired)
                {
                    Invoke(new Action(() => {
                        Invalidate();  // Force repaint
                        Update();      // Process the paint message immediately
                    }));
                }
                else
                {
                    Invalidate();  // Force repaint
                    Update();      // Process the paint message immediately
                }
                
                _logger.LogInformation("Spectrum window settings updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update spectrum window settings");
            }
        }

        /// <summary>
        /// Loads the application icon.
        /// </summary>
        private Icon LoadApplicationIcon()
        {
            try
            {
                // Try to load the custom icon from resources
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
                
                if (File.Exists(iconPath))
                {
                    var icon = new Icon(iconPath);
                    _logger.LogDebug("Loaded custom application icon from: {IconPath}", iconPath);
                    return icon;
                }
                else
                {
                    _logger.LogWarning("Custom icon not found at: {IconPath}, using embedded resource", iconPath);
                    
                    // Try to load from embedded resources
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    var resourceName = "TaskbarEqualizer.Main.Resources.app.ico";
                    
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        var icon = new Icon(stream);
                        _logger.LogDebug("Loaded application icon from embedded resource: {ResourceName}", resourceName);
                        return icon;
                    }
                    else
                    {
                        _logger.LogWarning("Embedded icon resource not found: {ResourceName}, falling back to system icon", resourceName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load custom application icon, falling back to system icon");
            }
            
            // Fallback to system application icon
            _logger.LogDebug("Using system application icon as fallback");
            return SystemIcons.Application;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshTimer?.Stop();
                _refreshTimer?.Dispose();
                _refreshTimer = null;
            }
            base.Dispose(disposing);
        }
    }
}