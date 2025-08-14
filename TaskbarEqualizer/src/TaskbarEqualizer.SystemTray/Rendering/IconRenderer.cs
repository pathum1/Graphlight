using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Core.Interfaces;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.SystemTray.Rendering
{
    /// <summary>
    /// High-performance icon renderer optimized for real-time equalizer visualization.
    /// Implements Windows 11 Fluent Design principles with 60 FPS target performance.
    /// </summary>
    public sealed class IconRenderer : IIconRenderer
    {
        private readonly ILogger<IconRenderer> _logger;
        private readonly GraphicsResourcePool _resourcePool;
        private readonly RenderCache _renderCache;
        private readonly PerformanceTracker _performanceTracker;
        
        private RenderConfiguration _configuration;
        private IconSize _iconSize;
        private VisualizationTheme? _currentTheme;
        
        private volatile bool _isInitialized;
        private volatile bool _disposed;
        
        // Animation state
        private double[]? _previousSpectrum;
        private double[]? _currentSpectrum;
        private double[]? _targetSpectrum;
        
        // Performance optimization
        private readonly object _renderLock = new object();
        private DateTime _lastRenderTime = DateTime.MinValue;
        private readonly TimeSpan _minRenderInterval;

        /// <summary>
        /// Initializes a new instance of the IconRenderer.
        /// </summary>
        /// <param name="logger">Logger for diagnostic information.</param>
        public IconRenderer(ILogger<IconRenderer> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _resourcePool = new GraphicsResourcePool();
            _renderCache = new RenderCache(maxCacheSize: 100);
            _performanceTracker = new PerformanceTracker();
            
            _configuration = new RenderConfiguration();
            _iconSize = IconSize.Medium;
            _minRenderInterval = TimeSpan.FromMilliseconds(1000.0 / _configuration.TargetFrameRate);
            
            _logger.LogDebug("IconRenderer initialized with default configuration");
        }

        #region Events

        /// <inheritdoc />
        public event EventHandler<RenderingCompletedEventArgs>? RenderingCompleted;

        #endregion

        #region Properties

        /// <inheritdoc />
        public RenderConfiguration Configuration => _configuration;

        /// <inheritdoc />
        public IconSize IconSize => _iconSize;

        /// <inheritdoc />
        public RenderingMetrics CurrentMetrics => _performanceTracker.GetCurrentMetrics();

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public async Task InitializeAsync(RenderConfiguration configuration, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IconRenderer));

            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            _logger.LogInformation("Initializing IconRenderer with configuration: {IconSize}, {Style}, {Quality}",
                configuration.IconSize, configuration.Style, configuration.Quality);

            try
            {
                _configuration = configuration;
                _iconSize = configuration.IconSize;
                
                // Initialize spectrum buffers
                int spectrumSize = GetOptimalSpectrumSize(_iconSize);
                _previousSpectrum = new double[spectrumSize];
                _currentSpectrum = new double[spectrumSize];
                _targetSpectrum = new double[spectrumSize];

                // Pre-warm the resource pool
                await _resourcePool.PrewarmAsync(cancellationToken);

                // Initialize render cache
                _renderCache.Initialize(_iconSize, _configuration.Quality);

                _isInitialized = true;
                _logger.LogInformation("IconRenderer initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize IconRenderer");
                throw;
            }

            await Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task<Icon> RenderEqualizerIconAsync(SpectrumDataEventArgs spectrumData, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => RenderEqualizerIcon(spectrumData), cancellationToken);
        }

        /// <inheritdoc />
        public Icon RenderEqualizerIcon(SpectrumDataEventArgs spectrumData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IconRenderer));

            if (!_isInitialized)
                throw new InvalidOperationException("IconRenderer must be initialized before rendering");

            if (spectrumData == null)
                throw new ArgumentNullException(nameof(spectrumData));

            var startTime = Environment.TickCount64;
            Icon? resultIcon = null;
            bool wasSkipped = false;
            string? skipReason = null;

            try
            {
                lock (_renderLock)
                {
                    // Check if we should skip this frame for performance
                    if (ShouldSkipFrame(spectrumData, out skipReason))
                    {
                        wasSkipped = true;
                        // Return cached icon or create minimal icon
                        resultIcon = _renderCache.GetLastRenderedIcon() ?? CreateFallbackIcon();
                    }
                    else
                    {
                        // Update animation state
                        UpdateAnimationState(spectrumData.Spectrum, spectrumData.BandCount);

                        // Check cache first
                        var cacheKey = GenerateCacheKey(_currentSpectrum!, _configuration);
                        resultIcon = _renderCache.GetCachedIcon(cacheKey);

                        if (resultIcon == null)
                        {
                            // Render new icon
                            resultIcon = RenderIconInternal(_currentSpectrum!, _configuration);
                            
                            // Cache the result
                            _renderCache.CacheIcon(cacheKey, resultIcon);
                        }

                        _lastRenderTime = DateTime.UtcNow;
                    }
                }

                // Update performance tracking
                var renderTime = TimeSpan.FromMilliseconds(Environment.TickCount64 - startTime);
                _performanceTracker.RecordFrame(renderTime, wasSkipped);

                // Fire completion event
                var completedArgs = new RenderingCompletedEventArgs(resultIcon, renderTime, spectrumData, wasSkipped, skipReason);
                RenderingCompleted?.Invoke(this, completedArgs);

                return resultIcon;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rendering equalizer icon");
                
                // Return fallback icon on error
                resultIcon = CreateFallbackIcon();
                var renderTime = TimeSpan.FromMilliseconds(Environment.TickCount64 - startTime);
                var completedArgs = new RenderingCompletedEventArgs(resultIcon, renderTime, spectrumData, true, "Error occurred");
                RenderingCompleted?.Invoke(this, completedArgs);
                
                return resultIcon;
            }
        }

        /// <inheritdoc />
        public async Task UpdateConfigurationAsync(RenderConfiguration configuration, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IconRenderer));

            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            _logger.LogInformation("Updating IconRenderer configuration - Colors: Primary={Primary}, Secondary={Secondary}, UseCustomColors implied by config", 
                configuration.ColorScheme?.PrimaryColor, configuration.ColorScheme?.SecondaryColor);

            try
            {
                lock (_renderLock)
                {
                    var needsSpectrumResize = configuration.IconSize != _configuration.IconSize;
                    var oldConfigHash = _configuration.GetHashCode();
                    var newConfigHash = configuration.GetHashCode();
                    var configChanged = oldConfigHash != newConfigHash;
                    
                    _configuration = configuration;
                    
                    if (needsSpectrumResize)
                    {
                        _iconSize = configuration.IconSize;
                        
                        // Resize spectrum buffers
                        int spectrumSize = GetOptimalSpectrumSize(_iconSize);
                        _previousSpectrum = new double[spectrumSize];
                        _currentSpectrum = new double[spectrumSize];
                        _targetSpectrum = new double[spectrumSize];
                    }
                    
                    // Clear cache when configuration changes (especially important for color changes)
                    if (configChanged)
                    {
                        _logger.LogDebug("Configuration hash changed from {OldHash} to {NewHash} - clearing render cache", 
                            oldConfigHash, newConfigHash);
                        _renderCache.Clear();
                    }
                }

                _logger.LogDebug("IconRenderer configuration updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update IconRenderer configuration");
                throw;
            }

            await Task.CompletedTask;
        }

        /// <inheritdoc />
        public void SetIconSize(IconSize iconSize)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IconRenderer));

            if (_iconSize == iconSize)
                return;

            _logger.LogDebug("Setting icon size to {IconSize}", iconSize);

            lock (_renderLock)
            {
                _iconSize = iconSize;
                _configuration.IconSize = iconSize;

                // Resize spectrum buffers
                int spectrumSize = GetOptimalSpectrumSize(iconSize);
                _previousSpectrum = new double[spectrumSize];
                _currentSpectrum = new double[spectrumSize];
                _targetSpectrum = new double[spectrumSize];

                // Clear cache as size changed
                _renderCache.Clear();
            }
        }

        /// <inheritdoc />
        public void ApplyTheme(VisualizationTheme theme)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IconRenderer));

            if (theme == null)
                throw new ArgumentNullException(nameof(theme));

            _logger.LogInformation("Applying visualization theme: {ThemeName}", theme.Name);

            lock (_renderLock)
            {
                _currentTheme = theme;
                
                // Clear cache as theme changed
                _renderCache.Clear();
            }

            _logger.LogDebug("Visualization theme applied successfully");
        }

        /// <inheritdoc />
        public void ClearCache()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IconRenderer));

            _logger.LogDebug("Clearing render cache");
            _renderCache.Clear();
        }

        /// <inheritdoc />
        public DetailedRenderingMetrics GetDetailedMetrics()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IconRenderer));

            return _performanceTracker.GetDetailedMetrics();
        }

        #endregion

        #region Private Methods

        private bool ShouldSkipFrame(SpectrumDataEventArgs spectrumData, out string? skipReason)
        {
            skipReason = null;

            // Check minimum render interval for frame rate limiting
            var timeSinceLastRender = DateTime.UtcNow - _lastRenderTime;
            if (timeSinceLastRender < _minRenderInterval)
            {
                skipReason = "Frame rate limiting";
                return true;
            }

            // Check if spectrum change is significant enough
            if (_previousSpectrum != null && IsSpectrumSimilar(spectrumData.Spectrum, _previousSpectrum, _configuration.ChangeThreshold))
            {
                skipReason = "Insufficient change";
                return true;
            }

            // Check performance constraints
            if (_configuration.AdaptiveQuality && _performanceTracker.IsPerformanceConstrained())
            {
                skipReason = "Performance constraints";
                return true;
            }

            return false;
        }

        private void UpdateAnimationState(double[] newSpectrum, int bandCount)
        {
            if (_currentSpectrum == null || _targetSpectrum == null)
                return;

            // Copy previous state
            if (_previousSpectrum != null)
            {
                Array.Copy(_currentSpectrum, _previousSpectrum, Math.Min(_currentSpectrum.Length, _previousSpectrum.Length));
            }

            // Resize if needed
            int targetSize = Math.Min(newSpectrum.Length, _currentSpectrum.Length);
            
            // Apply smoothing
            var smoothingFactor = _configuration.Animation.SmoothingFactor;
            for (int i = 0; i < targetSize; i++)
            {
                var targetValue = i < bandCount ? newSpectrum[i] : 0.0;
                var currentValue = _currentSpectrum[i];
                
                // Apply exponential smoothing
                _currentSpectrum[i] = smoothingFactor * targetValue + (1 - smoothingFactor) * currentValue;
            }
        }

        private Icon RenderIconInternal(double[] spectrum, RenderConfiguration config)
        {
            var iconSize = (int)config.IconSize;
            
            using var bitmap = new Bitmap(iconSize, iconSize, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            
            // Configure graphics for quality
            ConfigureGraphicsQuality(graphics, config.Quality);
            
            // Clear with transparent background
            graphics.Clear(Color.Transparent);
            
            // Render based on style
            switch (config.Style)
            {
                case EqualizerStyle.Bars:
                    RenderBarsStyle(graphics, spectrum, config, iconSize);
                    break;
                case EqualizerStyle.Dots:
                    RenderDotsStyle(graphics, spectrum, config, iconSize);
                    break;
                case EqualizerStyle.Waveform:
                    RenderWaveformStyle(graphics, spectrum, config, iconSize);
                    break;
                case EqualizerStyle.Spectrum:
                    RenderSpectrumStyle(graphics, spectrum, config, iconSize);
                    break;
                case EqualizerStyle.Lines:
                    RenderLinesStyle(graphics, spectrum, config, iconSize);
                    break;
                default:
                    RenderBarsStyle(graphics, spectrum, config, iconSize);
                    break;
            }
            
            // Convert to icon
            return Icon.FromHandle(bitmap.GetHicon());
        }

        private void ConfigureGraphicsQuality(Graphics graphics, RenderQuality quality)
        {
            switch (quality)
            {
                case RenderQuality.Low:
                    graphics.SmoothingMode = SmoothingMode.HighSpeed;
                    graphics.InterpolationMode = InterpolationMode.Low;
                    graphics.CompositingQuality = CompositingQuality.HighSpeed;
                    break;
                case RenderQuality.Medium:
                    graphics.SmoothingMode = SmoothingMode.Default;
                    graphics.InterpolationMode = InterpolationMode.Default;
                    graphics.CompositingQuality = CompositingQuality.Default;
                    break;
                case RenderQuality.High:
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    break;
                case RenderQuality.Ultra:
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    break;
            }
        }

        private void RenderBarsStyle(Graphics graphics, double[] spectrum, RenderConfiguration config, int iconSize)
        {
            // Use config color scheme directly - don't override with theme if custom colors are specified
            var colorScheme = config.ColorScheme;
            _logger.LogTrace("Using color scheme: Primary={Primary}, Secondary={Secondary}", 
                colorScheme.PrimaryColor, colorScheme.SecondaryColor);
            
            var barCount = Math.Min(spectrum.Length, GetOptimalBarCount(iconSize));
            
            var barWidth = (float)iconSize / barCount * 0.8f; // 80% width with spacing
            var barSpacing = (float)iconSize / barCount * 0.2f;
            var maxHeight = iconSize - 4; // Leave some margin
            
            for (int i = 0; i < barCount; i++)
            {
                var intensity = i < spectrum.Length ? spectrum[i] : 0.0;
                var barHeight = (float)(intensity * maxHeight);
                
                var x = i * (barWidth + barSpacing) + 2;
                var y = iconSize - barHeight - 2;
                
                var barRect = new RectangleF(x, y, barWidth, barHeight);
                
                // Create gradient brush if enabled
                using var brush = CreateBarBrush(barRect, colorScheme, intensity);
                
                // Draw rounded rectangle for Windows 11 style
                if (config.EnableEffects && barHeight > 4)
                {
                    using var path = CreateRoundedRectangle(barRect, 1.5f);
                    graphics.FillPath(brush, path);
                }
                else
                {
                    graphics.FillRectangle(brush, barRect);
                }
            }
        }

        private void RenderDotsStyle(Graphics graphics, double[] spectrum, RenderConfiguration config, int iconSize)
        {
            // Use config color scheme directly - don't override with theme
            var colorScheme = config.ColorScheme;
            var dotCount = Math.Min(spectrum.Length, GetOptimalBarCount(iconSize));
            
            var dotSize = (float)iconSize / dotCount * 0.6f;
            var spacing = (float)iconSize / dotCount;
            
            for (int i = 0; i < dotCount; i++)
            {
                var intensity = i < spectrum.Length ? spectrum[i] : 0.0;
                var alpha = (int)(intensity * 255);
                
                var x = i * spacing + spacing / 2 - dotSize / 2;
                var y = iconSize / 2f - dotSize / 2;
                
                var color = Color.FromArgb(alpha, colorScheme.PrimaryColor);
                using var brush = new SolidBrush(color);
                
                graphics.FillEllipse(brush, x, y, dotSize, dotSize);
            }
        }

        private void RenderWaveformStyle(Graphics graphics, double[] spectrum, RenderConfiguration config, int iconSize)
        {
            // Use config color scheme directly - don't override with theme
            var colorScheme = config.ColorScheme;
            
            if (spectrum.Length < 2) return;
            
            var points = new PointF[spectrum.Length];
            var centerY = iconSize / 2f;
            var amplitude = iconSize / 4f;
            
            for (int i = 0; i < spectrum.Length; i++)
            {
                var x = (float)i / (spectrum.Length - 1) * (iconSize - 4) + 2;
                var y = centerY - (float)(spectrum[i] * amplitude);
                points[i] = new PointF(x, y);
            }
            
            using var pen = new Pen(colorScheme.PrimaryColor, 2f);
            if (config.EnableEffects)
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
            }
            
            graphics.DrawCurve(pen, points);
        }

        private void RenderSpectrumStyle(Graphics graphics, double[] spectrum, RenderConfiguration config, int iconSize)
        {
            // Similar to bars but with more frequency bands and different coloring
            RenderBarsStyle(graphics, spectrum, config, iconSize);
        }

        private void RenderLinesStyle(Graphics graphics, double[] spectrum, RenderConfiguration config, int iconSize)
        {
            // Use config color scheme directly - don't override with theme
            var colorScheme = config.ColorScheme;
            var lineCount = Math.Min(spectrum.Length, GetOptimalBarCount(iconSize));
            
            var lineSpacing = (float)iconSize / lineCount;
            var maxHeight = iconSize - 4;
            
            using var pen = new Pen(colorScheme.PrimaryColor, 1.5f);
            
            for (int i = 0; i < lineCount; i++)
            {
                var intensity = i < spectrum.Length ? spectrum[i] : 0.0;
                var lineHeight = (float)(intensity * maxHeight);
                
                var x = i * lineSpacing + lineSpacing / 2;
                var y1 = iconSize - 2;
                var y2 = iconSize - lineHeight - 2;
                
                graphics.DrawLine(pen, x, y1, x, y2);
            }
        }

        private Brush CreateBarBrush(RectangleF rect, ColorScheme colorScheme, double intensity)
        {
            if (colorScheme.UseGradient && rect.Height > 4)
            {
                var color1 = colorScheme.SecondaryColor;
                var color2 = colorScheme.PrimaryColor;
                
                return colorScheme.GradientDirection switch
                {
                    GradientDirection.Vertical => new LinearGradientBrush(rect, color1, color2, LinearGradientMode.Vertical),
                    GradientDirection.Horizontal => new LinearGradientBrush(rect, color1, color2, LinearGradientMode.Horizontal),
                    GradientDirection.Diagonal => new LinearGradientBrush(rect, color1, color2, LinearGradientMode.ForwardDiagonal),
                    GradientDirection.Radial => new PathGradientBrush(new[] { 
                        new PointF(rect.X, rect.Y), 
                        new PointF(rect.Right, rect.Y), 
                        new PointF(rect.Right, rect.Bottom), 
                        new PointF(rect.X, rect.Bottom) 
                    }),
                    _ => new SolidBrush(colorScheme.PrimaryColor)
                };
            }
            else
            {
                var alpha = (int)(255 * Math.Min(1.0, intensity + 0.2)); // Ensure minimum visibility
                var color = Color.FromArgb(alpha, colorScheme.PrimaryColor);
                return new SolidBrush(color);
            }
        }

        private GraphicsPath CreateRoundedRectangle(RectangleF rect, float cornerRadius)
        {
            var path = new GraphicsPath();
            var diameter = cornerRadius * 2;
            
            if (rect.Width < diameter || rect.Height < diameter)
            {
                path.AddRectangle(rect);
                return path;
            }
            
            // Add rounded corners
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            
            return path;
        }

        private Icon CreateFallbackIcon()
        {
            var iconSize = (int)_iconSize;
            using var bitmap = new Bitmap(iconSize, iconSize, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            
            graphics.Clear(Color.Transparent);
            
            // Draw a simple static icon
            var color = Color.FromArgb(100, 0, 120, 215); // Semi-transparent blue
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, 2, 2, iconSize - 4, iconSize - 4);
            
            return Icon.FromHandle(bitmap.GetHicon());
        }

        private string GenerateCacheKey(double[] spectrum, RenderConfiguration config)
        {
            // Use improved config hash that includes color properties
            var configHash = config.GetHashCode();
            
            // Generate spectrum hash with better precision for color changes
            var spectrumHash = 0;
            for (int i = 0; i < Math.Min(spectrum.Length, 16); i++) // Limit for performance
            {
                spectrumHash = HashCode.Combine(spectrumHash, (int)(spectrum[i] * 1000));
            }
            
            // Combine config hash (which now includes colors) with spectrum data
            var finalHash = HashCode.Combine(configHash, spectrumHash);
            
            _logger.LogTrace("Generated cache key {CacheKey} for config hash {ConfigHash} and spectrum hash {SpectrumHash}", 
                finalHash, configHash, spectrumHash);
                
            return finalHash.ToString();
        }

        private bool IsSpectrumSimilar(double[] spectrum1, double[] spectrum2, double threshold)
        {
            if (spectrum1.Length != spectrum2.Length)
                return false;

            double totalDifference = 0;
            int compareLength = Math.Min(spectrum1.Length, spectrum2.Length);
            
            for (int i = 0; i < compareLength; i++)
            {
                totalDifference += Math.Abs(spectrum1[i] - spectrum2[i]);
            }

            var averageDifference = totalDifference / compareLength;
            return averageDifference < threshold;
        }

        private int GetOptimalSpectrumSize(IconSize iconSize)
        {
            return iconSize switch
            {
                IconSize.Small => 4,    // 16x16 - minimal detail
                IconSize.Medium => 8,   // 24x24 - balanced
                IconSize.Large => 16,   // 32x32 - standard
                IconSize.ExtraLarge => 32, // 48x48 - high detail
                _ => 16
            };
        }

        private int GetOptimalBarCount(int iconSize)
        {
            return iconSize switch
            {
                <= 16 => 4,
                <= 24 => 6,
                <= 32 => 12,
                _ => 16
            };
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the icon renderer and releases all resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                _resourcePool?.Dispose();
                _renderCache?.Dispose();
                _performanceTracker?.Dispose();

                _logger.LogDebug("IconRenderer disposed");
            }
        }

        #endregion
    }
}