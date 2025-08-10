using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Core.Interfaces;
using TaskbarEqualizer.Configuration;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.Main
{
    /// <summary>
    /// Main spectrum analyzer window that displays the audio visualization.
    /// </summary>
    public partial class SpectrumAnalyzerWindow : Form
    {
        private readonly ILogger<SpectrumAnalyzerWindow> _logger;
        private SpectrumDataEventArgs? _currentSpectrum;
        private Timer? _refreshTimer;
        private float[] _smoothedSpectrum = new float[16]; // Default to 16 bands from ApplicationSettings
        private float _smoothingFactor = 0.8f; // Default from ApplicationSettings
        private double _gainFactor = 1.0; // Default from ApplicationSettings
        private ApplicationSettings? _settings;

        public SpectrumAnalyzerWindow(ILogger<SpectrumAnalyzerWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                Icon = SystemIcons.Application;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not set window icon");
            }

            // Handle window closing
            FormClosing += SpectrumAnalyzerWindow_FormClosing;
            
            // Handle resize
            Resize += SpectrumAnalyzerWindow_Resize;
            
            
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
        }

        private void SpectrumAnalyzerWindow_FormClosing(object? sender, FormClosingEventArgs e)
        {
            // Minimize to tray instead of closing
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                _logger.LogInformation("Window minimized to system tray");
            }
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

                // Force a redraw with new settings
                Invalidate();
                
                _logger.LogInformation("Spectrum window settings updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update spectrum window settings");
            }
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