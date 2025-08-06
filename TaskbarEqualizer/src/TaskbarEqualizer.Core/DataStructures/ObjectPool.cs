using System;
using System.Collections.Concurrent;
using System.Threading;

namespace TaskbarEqualizer.Core.DataStructures
{
    /// <summary>
    /// High-performance object pool implementation designed to eliminate garbage collection pressure
    /// in real-time audio processing scenarios. Thread-safe and optimized for frequent allocations.
    /// </summary>
    /// <typeparam name="T">Type of objects to pool.</typeparam>
    public sealed class ObjectPool<T> : IDisposable where T : class
    {
        private readonly ConcurrentQueue<T> _objects;
        private readonly Func<T> _objectFactory;
        private readonly Action<T>? _resetAction;
        private readonly int _maxSize;
        private volatile int _currentCount;
        private volatile bool _disposed;

        /// <summary>
        /// Initializes a new instance of the ObjectPool.
        /// </summary>
        /// <param name="objectFactory">Factory function to create new objects.</param>
        /// <param name="resetAction">Optional action to reset objects before returning to pool.</param>
        /// <param name="maxSize">Maximum number of objects to keep in the pool.</param>
        /// <param name="preloadCount">Number of objects to create immediately.</param>
        public ObjectPool(Func<T> objectFactory, Action<T>? resetAction = null, int maxSize = 100, int preloadCount = 10)
        {
            _objectFactory = objectFactory ?? throw new ArgumentNullException(nameof(objectFactory));
            _resetAction = resetAction;
            _maxSize = maxSize > 0 ? maxSize : 100;
            _objects = new ConcurrentQueue<T>();
            _currentCount = 0;
            _disposed = false;

            // Preload the pool with objects
            for (int i = 0; i < Math.Min(preloadCount, _maxSize); i++)
            {
                var obj = _objectFactory();
                _objects.Enqueue(obj);
                Interlocked.Increment(ref _currentCount);
            }
        }

        /// <summary>
        /// Gets the current number of objects in the pool.
        /// </summary>
        public int Count => _currentCount;

        /// <summary>
        /// Gets the maximum capacity of the pool.
        /// </summary>
        public int MaxSize => _maxSize;

        /// <summary>
        /// Gets an object from the pool or creates a new one if the pool is empty.
        /// </summary>
        /// <returns>An object instance ready for use.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the pool has been disposed.</exception>
        public T Get()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ObjectPool<T>));

            if (_objects.TryDequeue(out T? obj))
            {
                Interlocked.Decrement(ref _currentCount);
                return obj;
            }

            // Pool is empty, create a new object
            return _objectFactory();
        }

        /// <summary>
        /// Returns an object to the pool for reuse.
        /// </summary>
        /// <param name="obj">Object to return to the pool.</param>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the pool has been disposed.</exception>
        public void Return(T obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            if (_disposed)
                return; // Silently ignore returns after disposal

            // Apply reset action if configured
            try
            {
                _resetAction?.Invoke(obj);
            }
            catch
            {
                // If reset fails, don't return the object to the pool
                return;
            }

            // Only add to pool if we haven't exceeded the maximum size
            if (_currentCount < _maxSize)
            {
                _objects.Enqueue(obj);
                Interlocked.Increment(ref _currentCount);
            }
            // If pool is full, let the object be garbage collected
        }

        /// <summary>
        /// Clears all objects from the pool.
        /// </summary>
        public void Clear()
        {
            if (_disposed)
                return;

            while (_objects.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _currentCount);
            }
        }

        /// <summary>
        /// Disposes the pool and releases all pooled objects.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Clear();

                // If objects implement IDisposable, dispose them
                while (_objects.TryDequeue(out T? obj))
                {
                    if (obj is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Specialized object pool for audio sample arrays with optimized memory management.
    /// </summary>
    public sealed class AudioSamplePool : IDisposable
    {
        private readonly ObjectPool<float[]> _pool;
        private readonly int _sampleSize;

        /// <summary>
        /// Initializes a new instance of the AudioSamplePool.
        /// </summary>
        /// <param name="sampleSize">Size of audio sample arrays.</param>
        /// <param name="maxPoolSize">Maximum number of arrays to keep in pool.</param>
        /// <param name="preloadCount">Number of arrays to create immediately.</param>
        public AudioSamplePool(int sampleSize, int maxPoolSize = 50, int preloadCount = 10)
        {
            _sampleSize = sampleSize;
            _pool = new ObjectPool<float[]>(
                objectFactory: () => new float[sampleSize],
                resetAction: array => Array.Clear(array, 0, array.Length),
                maxSize: maxPoolSize,
                preloadCount: preloadCount
            );
        }

        /// <summary>
        /// Gets the size of arrays managed by this pool.
        /// </summary>
        public int SampleSize => _sampleSize;

        /// <summary>
        /// Gets the current number of arrays in the pool.
        /// </summary>
        public int Count => _pool.Count;

        /// <summary>
        /// Gets a cleared audio sample array from the pool.
        /// </summary>
        /// <returns>A float array ready for audio data.</returns>
        public float[] Get() => _pool.Get();

        /// <summary>
        /// Returns an audio sample array to the pool.
        /// The array will be automatically cleared before reuse.
        /// </summary>
        /// <param name="samples">Array to return to the pool.</param>
        /// <exception cref="ArgumentException">Thrown when array size doesn't match pool size.</exception>
        public void Return(float[] samples)
        {
            if (samples == null)
                throw new ArgumentNullException(nameof(samples));

            if (samples.Length != _sampleSize)
                throw new ArgumentException($"Array size {samples.Length} doesn't match pool size {_sampleSize}.", nameof(samples));

            _pool.Return(samples);
        }

        /// <summary>
        /// Clears all arrays from the pool.
        /// </summary>
        public void Clear() => _pool.Clear();

        /// <summary>
        /// Disposes the pool and releases all resources.
        /// </summary>
        public void Dispose() => _pool.Dispose();
    }

    /// <summary>
    /// Specialized object pool for spectrum data arrays used in frequency analysis.
    /// </summary>
    public sealed class SpectrumDataPool : IDisposable
    {
        private readonly ObjectPool<double[]> _pool;
        private readonly int _bandCount;

        /// <summary>
        /// Initializes a new instance of the SpectrumDataPool.
        /// </summary>
        /// <param name="bandCount">Number of frequency bands.</param>
        /// <param name="maxPoolSize">Maximum number of arrays to keep in pool.</param>
        /// <param name="preloadCount">Number of arrays to create immediately.</param>
        public SpectrumDataPool(int bandCount, int maxPoolSize = 30, int preloadCount = 5)
        {
            _bandCount = bandCount;
            _pool = new ObjectPool<double[]>(
                objectFactory: () => new double[bandCount],
                resetAction: array => Array.Clear(array, 0, array.Length),
                maxSize: maxPoolSize,
                preloadCount: preloadCount
            );
        }

        /// <summary>
        /// Gets the number of frequency bands managed by this pool.
        /// </summary>
        public int BandCount => _bandCount;

        /// <summary>
        /// Gets the current number of arrays in the pool.
        /// </summary>
        public int Count => _pool.Count;

        /// <summary>
        /// Gets a cleared spectrum data array from the pool.
        /// </summary>
        /// <returns>A double array ready for spectrum data.</returns>
        public double[] Get() => _pool.Get();

        /// <summary>
        /// Returns a spectrum data array to the pool.
        /// The array will be automatically cleared before reuse.
        /// </summary>
        /// <param name="spectrum">Array to return to the pool.</param>
        /// <exception cref="ArgumentException">Thrown when array size doesn't match pool size.</exception>
        public void Return(double[] spectrum)
        {
            if (spectrum == null)
                throw new ArgumentNullException(nameof(spectrum));

            if (spectrum.Length != _bandCount)
                throw new ArgumentException($"Array size {spectrum.Length} doesn't match pool size {_bandCount}.", nameof(spectrum));

            _pool.Return(spectrum);
        }

        /// <summary>
        /// Clears all arrays from the pool.
        /// </summary>
        public void Clear() => _pool.Clear();

        /// <summary>
        /// Disposes the pool and releases all resources.
        /// </summary>
        public void Dispose() => _pool.Dispose();
    }

    /// <summary>
    /// Factory for creating commonly-used object pools with optimal configurations.
    /// </summary>
    public static class PoolFactory
    {
        /// <summary>
        /// Creates an audio sample pool optimized for the specified configuration.
        /// </summary>
        /// <param name="sampleRate">Audio sample rate (e.g., 44100).</param>
        /// <param name="bufferSizeMs">Buffer size in milliseconds.</param>
        /// <param name="channelCount">Number of audio channels.</param>
        /// <returns>Optimized audio sample pool.</returns>
        public static AudioSamplePool CreateAudioSamplePool(int sampleRate, double bufferSizeMs, int channelCount = 1)
        {
            int sampleSize = (int)Math.Ceiling(sampleRate * bufferSizeMs / 1000.0) * channelCount;
            int poolSize = Math.Max(20, (int)(1000.0 / bufferSizeMs * 2)); // 2 seconds worth of buffers
            int preloadCount = Math.Min(poolSize / 2, 10);

            return new AudioSamplePool(sampleSize, poolSize, preloadCount);
        }

        /// <summary>
        /// Creates an audio sample pool sized to match FFT requirements.
        /// Ensures sample buffers can accommodate the maximum FFT size.
        /// </summary>
        /// <param name="fftSize">FFT size in samples.</param>
        /// <param name="channelCount">Number of audio channels.</param>
        /// <returns>Audio sample pool with FFT-sized buffers.</returns>
        public static AudioSamplePool CreateAudioSamplePoolForFft(int fftSize, int channelCount = 1)
        {
            int sampleSize = fftSize * channelCount;
            int poolSize = 50; // Sufficient pool size for real-time processing
            int preloadCount = 10;

            return new AudioSamplePool(sampleSize, poolSize, preloadCount);
        }

        /// <summary>
        /// Creates a spectrum data pool optimized for the specified frequency band count.
        /// </summary>
        /// <param name="bandCount">Number of frequency bands.</param>
        /// <param name="updateRateHz">Update rate in Hz (e.g., 60 for 60 FPS).</param>
        /// <returns>Optimized spectrum data pool.</returns>
        public static SpectrumDataPool CreateSpectrumDataPool(int bandCount, int updateRateHz = 60)
        {
            int poolSize = Math.Max(15, updateRateHz * 2); // 2 seconds worth of updates
            int preloadCount = Math.Min(poolSize / 3, 5);

            return new SpectrumDataPool(bandCount, poolSize, preloadCount);
        }
    }
}