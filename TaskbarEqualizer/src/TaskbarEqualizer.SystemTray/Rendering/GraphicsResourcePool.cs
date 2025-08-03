using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TaskbarEqualizer.SystemTray.Rendering
{
    /// <summary>
    /// High-performance graphics resource pool for icon rendering.
    /// Manages reusable GDI+ objects to minimize allocations and GC pressure.
    /// </summary>
    public sealed class GraphicsResourcePool : IDisposable
    {
        private readonly ConcurrentQueue<Bitmap> _bitmapPool = new();
        private readonly ConcurrentQueue<Graphics> _graphicsPool = new();
        private readonly ConcurrentQueue<SolidBrush> _brushPool = new();
        private readonly ConcurrentQueue<LinearGradientBrush> _gradientBrushPool = new();
        private readonly ConcurrentQueue<Pen> _penPool = new();
        private readonly ConcurrentQueue<GraphicsPath> _pathPool = new();
        
        private readonly object _initializationLock = new();
        private volatile bool _disposed;
        private volatile bool _isInitialized;
        
        // Pool configuration
        private const int MaxBitmapPoolSize = 20;
        private const int MaxBrushPoolSize = 15;
        private const int MaxPenPoolSize = 10;
        private const int MaxPathPoolSize = 10;
        private const int MaxGraphicsPoolSize = 5;
        
        // Pool counters for monitoring
        private int _bitmapPoolCount;
        private int _brushPoolCount;
        private int _penPoolCount;
        private int _pathPoolCount;
        private int _graphicsPoolCount;
        
        // Statistics
        private long _totalAllocations;
        private long _poolHits;
        private long _poolMisses;

        /// <summary>
        /// Gets pool performance statistics.
        /// </summary>
        public PoolStatistics Statistics => new()
        {
            TotalAllocations = _totalAllocations,
            PoolHits = _poolHits,
            PoolMisses = _poolMisses,
            HitRate = _totalAllocations > 0 ? (double)_poolHits / _totalAllocations : 0.0,
            BitmapPoolSize = _bitmapPoolCount,
            BrushPoolSize = _brushPoolCount,
            PenPoolSize = _penPoolCount,
            PathPoolSize = _pathPoolCount,
            GraphicsPoolSize = _graphicsPoolCount
        };

        /// <summary>
        /// Pre-warms the resource pool with commonly used objects.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        public async Task PrewarmAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GraphicsResourcePool));

            if (_isInitialized)
                return;

            await Task.Run(() =>
            {
                lock (_initializationLock)
                {
                    if (_isInitialized)
                        return;

                    try
                    {
                        // Pre-create common bitmaps
                        for (int i = 0; i < 5; i++)
                        {
                            var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                            _bitmapPool.Enqueue(bitmap);
                            Interlocked.Increment(ref _bitmapPoolCount);
                        }

                        // Pre-create common brushes
                        for (int i = 0; i < 3; i++)
                        {
                            var brush = new SolidBrush(Color.Transparent);
                            _brushPool.Enqueue(brush);
                            Interlocked.Increment(ref _brushPoolCount);
                        }

                        // Pre-create common pens
                        for (int i = 0; i < 2; i++)
                        {
                            var pen = new Pen(Color.Black, 1.0f);
                            _penPool.Enqueue(pen);
                            Interlocked.Increment(ref _penPoolCount);
                        }

                        // Pre-create paths
                        for (int i = 0; i < 2; i++)
                        {
                            var path = new GraphicsPath();
                            _pathPool.Enqueue(path);
                            Interlocked.Increment(ref _pathPoolCount);
                        }

                        _isInitialized = true;
                    }
                    catch (Exception)
                    {
                        // Clean up any partially created resources
                        ClearPool();
                        throw;
                    }
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Gets a bitmap from the pool or creates a new one.
        /// </summary>
        /// <param name="width">Bitmap width.</param>
        /// <param name="height">Bitmap height.</param>
        /// <param name="pixelFormat">Pixel format for the bitmap.</param>
        /// <returns>Bitmap instance from pool or newly created.</returns>
        public Bitmap GetBitmap(int width, int height, System.Drawing.Imaging.PixelFormat pixelFormat = System.Drawing.Imaging.PixelFormat.Format32bppArgb)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GraphicsResourcePool));

            Interlocked.Increment(ref _totalAllocations);

            // Try to get from pool
            if (_bitmapPool.TryDequeue(out var pooledBitmap))
            {
                Interlocked.Decrement(ref _bitmapPoolCount);
                
                // Check if the pooled bitmap matches requirements
                if (pooledBitmap.Width == width && 
                    pooledBitmap.Height == height && 
                    pooledBitmap.PixelFormat == pixelFormat)
                {
                    Interlocked.Increment(ref _poolHits);
                    
                    // Clear the bitmap for reuse
                    using var graphics = Graphics.FromImage(pooledBitmap);
                    graphics.Clear(Color.Transparent);
                    
                    return pooledBitmap;
                }
                else
                {
                    // Dispose incompatible bitmap
                    pooledBitmap?.Dispose();
                }
            }

            // Create new bitmap
            Interlocked.Increment(ref _poolMisses);
            return new Bitmap(width, height, pixelFormat);
        }

        /// <summary>
        /// Returns a bitmap to the pool for reuse.
        /// </summary>
        /// <param name="bitmap">Bitmap to return to pool.</param>
        public void ReturnBitmap(Bitmap? bitmap)
        {
            if (bitmap == null || _disposed)
                return;

            try
            {
                // Only pool if we have room
                if (_bitmapPoolCount < MaxBitmapPoolSize)
                {
                    _bitmapPool.Enqueue(bitmap);
                    Interlocked.Increment(ref _bitmapPoolCount);
                }
                else
                {
                    bitmap.Dispose();
                }
            }
            catch
            {
                // Bitmap may already be disposed, ignore
            }
        }

        /// <summary>
        /// Gets a solid brush from the pool or creates a new one.
        /// </summary>
        /// <param name="color">Brush color.</param>
        /// <returns>SolidBrush instance from pool or newly created.</returns>
        public SolidBrush GetSolidBrush(Color color)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GraphicsResourcePool));

            Interlocked.Increment(ref _totalAllocations);

            // Try to get from pool
            if (_brushPool.TryDequeue(out var pooledBrush))
            {
                Interlocked.Decrement(ref _brushPoolCount);
                Interlocked.Increment(ref _poolHits);
                
                pooledBrush.Color = color;
                return pooledBrush;
            }

            // Create new brush
            Interlocked.Increment(ref _poolMisses);
            return new SolidBrush(color);
        }

        /// <summary>
        /// Returns a solid brush to the pool for reuse.
        /// </summary>
        /// <param name="brush">Brush to return to pool.</param>
        public void ReturnSolidBrush(SolidBrush? brush)
        {
            if (brush == null || _disposed)
                return;

            // Only pool if we have room
            if (_brushPoolCount < MaxBrushPoolSize)
            {
                _brushPool.Enqueue(brush);
                Interlocked.Increment(ref _brushPoolCount);
            }
            else
            {
                brush.Dispose();
            }
        }

        /// <summary>
        /// Gets a pen from the pool or creates a new one.
        /// </summary>
        /// <param name="color">Pen color.</param>
        /// <param name="width">Pen width.</param>
        /// <returns>Pen instance from pool or newly created.</returns>
        public Pen GetPen(Color color, float width = 1.0f)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GraphicsResourcePool));

            Interlocked.Increment(ref _totalAllocations);

            // Try to get from pool
            if (_penPool.TryDequeue(out var pooledPen))
            {
                Interlocked.Decrement(ref _penPoolCount);
                Interlocked.Increment(ref _poolHits);
                
                pooledPen.Color = color;
                pooledPen.Width = width;
                return pooledPen;
            }

            // Create new pen
            Interlocked.Increment(ref _poolMisses);
            return new Pen(color, width);
        }

        /// <summary>
        /// Returns a pen to the pool for reuse.
        /// </summary>
        /// <param name="pen">Pen to return to pool.</param>
        public void ReturnPen(Pen? pen)
        {
            if (pen == null || _disposed)
                return;

            // Reset pen properties for reuse
            pen.StartCap = LineCap.Flat;
            pen.EndCap = LineCap.Flat;
            pen.LineJoin = LineJoin.Miter;

            // Only pool if we have room
            if (_penPoolCount < MaxPenPoolSize)
            {
                _penPool.Enqueue(pen);
                Interlocked.Increment(ref _penPoolCount);
            }
            else
            {
                pen.Dispose();
            }
        }

        /// <summary>
        /// Gets a graphics path from the pool or creates a new one.
        /// </summary>
        /// <returns>GraphicsPath instance from pool or newly created.</returns>
        public GraphicsPath GetGraphicsPath()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GraphicsResourcePool));

            Interlocked.Increment(ref _totalAllocations);

            // Try to get from pool
            if (_pathPool.TryDequeue(out var pooledPath))
            {
                Interlocked.Decrement(ref _pathPoolCount);
                Interlocked.Increment(ref _poolHits);
                
                pooledPath.Reset();
                return pooledPath;
            }

            // Create new path
            Interlocked.Increment(ref _poolMisses);
            return new GraphicsPath();
        }

        /// <summary>
        /// Returns a graphics path to the pool for reuse.
        /// </summary>
        /// <param name="path">Path to return to pool.</param>
        public void ReturnGraphicsPath(GraphicsPath? path)
        {
            if (path == null || _disposed)
                return;

            // Only pool if we have room
            if (_pathPoolCount < MaxPathPoolSize)
            {
                _pathPool.Enqueue(path);
                Interlocked.Increment(ref _pathPoolCount);
            }
            else
            {
                path.Dispose();
            }
        }

        /// <summary>
        /// Clears all pooled resources.
        /// </summary>
        public void ClearPool()
        {
            // Clear bitmaps
            while (_bitmapPool.TryDequeue(out var bitmap))
            {
                bitmap?.Dispose();
                Interlocked.Decrement(ref _bitmapPoolCount);
            }

            // Clear brushes
            while (_brushPool.TryDequeue(out var brush))
            {
                brush?.Dispose();
                Interlocked.Decrement(ref _brushPoolCount);
            }

            // Clear pens
            while (_penPool.TryDequeue(out var pen))
            {
                pen?.Dispose();
                Interlocked.Decrement(ref _penPoolCount);
            }

            // Clear paths
            while (_pathPool.TryDequeue(out var path))
            {
                path?.Dispose();
                Interlocked.Decrement(ref _pathPoolCount);
            }

            // Clear graphics
            while (_graphicsPool.TryDequeue(out var graphics))
            {
                graphics?.Dispose();
                Interlocked.Decrement(ref _graphicsPoolCount);
            }

            // Reset statistics
            _totalAllocations = 0;
            _poolHits = 0;
            _poolMisses = 0;
        }

        /// <summary>
        /// Disposes the resource pool and all pooled objects.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                ClearPool();
            }
        }
    }

    /// <summary>
    /// Pool performance statistics.
    /// </summary>
    public class PoolStatistics
    {
        /// <summary>
        /// Total number of resource allocations.
        /// </summary>
        public long TotalAllocations { get; set; }

        /// <summary>
        /// Number of times resources were served from pool.
        /// </summary>
        public long PoolHits { get; set; }

        /// <summary>
        /// Number of times new resources had to be created.
        /// </summary>
        public long PoolMisses { get; set; }

        /// <summary>
        /// Pool hit rate (0.0-1.0).
        /// </summary>
        public double HitRate { get; set; }

        /// <summary>
        /// Current number of bitmaps in pool.
        /// </summary>
        public int BitmapPoolSize { get; set; }

        /// <summary>
        /// Current number of brushes in pool.
        /// </summary>
        public int BrushPoolSize { get; set; }

        /// <summary>
        /// Current number of pens in pool.
        /// </summary>
        public int PenPoolSize { get; set; }

        /// <summary>
        /// Current number of paths in pool.
        /// </summary>
        public int PathPoolSize { get; set; }

        /// <summary>
        /// Current number of graphics objects in pool.
        /// </summary>
        public int GraphicsPoolSize { get; set; }
    }
}