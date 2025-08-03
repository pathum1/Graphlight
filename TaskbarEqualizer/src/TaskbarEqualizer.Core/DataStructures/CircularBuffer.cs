using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace TaskbarEqualizer.Core.DataStructures
{
    /// <summary>
    /// High-performance circular buffer optimized for audio processing.
    /// Provides thread-safe access with minimal locking for real-time audio scenarios.
    /// </summary>
    /// <typeparam name="T">Type of elements stored in the buffer.</typeparam>
    public sealed unsafe class CircularBuffer<T> : IDisposable where T : unmanaged
    {
        private readonly T* _buffer;
        private readonly int _capacity;
        private readonly int _mask;
        private volatile int _writeIndex;
        private volatile int _readIndex;
        private volatile int _availableData;
        private volatile bool _disposed;
        private readonly object _lockObject = new object();

        /// <summary>
        /// Initializes a new instance of the CircularBuffer with the specified capacity.
        /// </summary>
        /// <param name="capacity">Capacity of the buffer (must be a power of 2 for optimal performance).</param>
        /// <exception cref="ArgumentException">Thrown when capacity is not a power of 2.</exception>
        public CircularBuffer(int capacity)
        {
            if (capacity <= 0 || (capacity & (capacity - 1)) != 0)
                throw new ArgumentException("Capacity must be a positive power of 2.", nameof(capacity));

            _capacity = capacity;
            _mask = capacity - 1;
            _buffer = (T*)System.Runtime.InteropServices.Marshal.AllocHGlobal(capacity * sizeof(T));
            _writeIndex = 0;
            _readIndex = 0;
            _availableData = 0;
            _disposed = false;

            // Initialize buffer to zero
            for (int i = 0; i < capacity; i++)
            {
                _buffer[i] = default(T);
            }
        }

        /// <summary>
        /// Gets the capacity of the buffer.
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// Gets the number of elements available for reading.
        /// </summary>
        public int AvailableData => _availableData;

        /// <summary>
        /// Gets the number of free spaces available for writing.
        /// </summary>
        public int AvailableSpace => _capacity - _availableData;

        /// <summary>
        /// Gets a value indicating whether the buffer is empty.
        /// </summary>
        public bool IsEmpty => _availableData == 0;

        /// <summary>
        /// Gets a value indicating whether the buffer is full.
        /// </summary>
        public bool IsFull => _availableData == _capacity;

        /// <summary>
        /// Writes data to the buffer. If there's insufficient space, existing data may be overwritten.
        /// </summary>
        /// <param name="data">Pointer to the data to write.</param>
        /// <param name="count">Number of elements to write.</param>
        /// <returns>Number of elements actually written.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the buffer has been disposed.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Write(T* data, int count)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CircularBuffer<T>));

            if (data == null || count <= 0)
                return 0;

            lock (_lockObject)
            {
                int writeIndex = _writeIndex;
                int written = 0;

                for (int i = 0; i < count; i++)
                {
                    _buffer[writeIndex] = data[i];
                    writeIndex = (writeIndex + 1) & _mask;
                    written++;

                    // If buffer becomes full, advance read index (overwrite old data)
                    if (_availableData == _capacity)
                    {
                        _readIndex = (_readIndex + 1) & _mask;
                    }
                    else
                    {
                        _availableData++;
                    }
                }

                _writeIndex = writeIndex;
                return written;
            }
        }

        /// <summary>
        /// Writes data from a managed array to the buffer.
        /// </summary>
        /// <param name="data">Array containing data to write.</param>
        /// <param name="offset">Offset in the array to start reading from.</param>
        /// <param name="count">Number of elements to write.</param>
        /// <returns>Number of elements actually written.</returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when offset or count is invalid.</exception>
        public int Write(T[] data, int offset = 0, int count = -1)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (offset < 0 || offset >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count == -1)
                count = data.Length - offset;

            if (count < 0 || offset + count > data.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            fixed (T* dataPtr = &data[offset])
            {
                return Write(dataPtr, count);
            }
        }

        /// <summary>
        /// Reads data from the buffer.
        /// </summary>
        /// <param name="data">Pointer to the buffer to read data into.</param>
        /// <param name="count">Maximum number of elements to read.</param>
        /// <returns>Number of elements actually read.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the buffer has been disposed.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Read(T* data, int count)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CircularBuffer<T>));

            if (data == null || count <= 0)
                return 0;

            lock (_lockObject)
            {
                int readIndex = _readIndex;
                int toRead = Math.Min(count, _availableData);

                for (int i = 0; i < toRead; i++)
                {
                    data[i] = _buffer[readIndex];
                    readIndex = (readIndex + 1) & _mask;
                }

                _readIndex = readIndex;
                _availableData -= toRead;

                return toRead;
            }
        }

        /// <summary>
        /// Reads data from the buffer into a managed array.
        /// </summary>
        /// <param name="data">Array to read data into.</param>
        /// <param name="offset">Offset in the array to start writing to.</param>
        /// <param name="count">Maximum number of elements to read.</param>
        /// <returns>Number of elements actually read.</returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when offset or count is invalid.</exception>
        public int Read(T[] data, int offset = 0, int count = -1)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (offset < 0 || offset >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count == -1)
                count = data.Length - offset;

            if (count < 0 || offset + count > data.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            fixed (T* dataPtr = &data[offset])
            {
                return Read(dataPtr, count);
            }
        }

        /// <summary>
        /// Peeks at data in the buffer without consuming it.
        /// </summary>
        /// <param name="data">Pointer to the buffer to copy peeked data into.</param>
        /// <param name="count">Maximum number of elements to peek.</param>
        /// <returns>Number of elements actually peeked.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the buffer has been disposed.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Peek(T* data, int count)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CircularBuffer<T>));

            if (data == null || count <= 0)
                return 0;

            lock (_lockObject)
            {
                int readIndex = _readIndex;
                int toPeek = Math.Min(count, _availableData);

                for (int i = 0; i < toPeek; i++)
                {
                    data[i] = _buffer[readIndex];
                    readIndex = (readIndex + 1) & _mask;
                }

                return toPeek;
            }
        }

        /// <summary>
        /// Skips the specified number of elements without reading them.
        /// </summary>
        /// <param name="count">Number of elements to skip.</param>
        /// <returns>Number of elements actually skipped.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the buffer has been disposed.</exception>
        public int Skip(int count)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CircularBuffer<T>));

            if (count <= 0)
                return 0;

            lock (_lockObject)
            {
                int toSkip = Math.Min(count, _availableData);
                _readIndex = (_readIndex + toSkip) & _mask;
                _availableData -= toSkip;
                return toSkip;
            }
        }

        /// <summary>
        /// Clears all data from the buffer.
        /// </summary>
        public void Clear()
        {
            if (_disposed)
                return;

            lock (_lockObject)
            {
                _readIndex = 0;
                _writeIndex = 0;
                _availableData = 0;
            }
        }

        /// <summary>
        /// Disposes the buffer and releases all resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)_buffer);
            }
        }
    }

    /// <summary>
    /// Specialized circular buffer for float audio samples with optimized performance.
    /// </summary>
    public sealed class AudioCircularBuffer : IDisposable
    {
        private readonly CircularBuffer<float> _buffer;
        private readonly float[] _tempBuffer;

        /// <summary>
        /// Initializes a new instance of the AudioCircularBuffer.
        /// </summary>
        /// <param name="capacity">Capacity in samples (must be power of 2).</param>
        public AudioCircularBuffer(int capacity)
        {
            _buffer = new CircularBuffer<float>(capacity);
            _tempBuffer = new float[Math.Min(capacity / 4, 4096)]; // Temp buffer for efficient transfers
        }

        /// <summary>
        /// Gets the capacity of the buffer in samples.
        /// </summary>
        public int Capacity => _buffer.Capacity;

        /// <summary>
        /// Gets the number of samples available for reading.
        /// </summary>
        public int AvailableSamples => _buffer.AvailableData;

        /// <summary>
        /// Gets the number of free sample slots available for writing.
        /// </summary>
        public int AvailableSpace => _buffer.AvailableSpace;

        /// <summary>
        /// Gets a value indicating whether the buffer is empty.
        /// </summary>
        public bool IsEmpty => _buffer.IsEmpty;

        /// <summary>
        /// Gets a value indicating whether the buffer is full.
        /// </summary>
        public bool IsFull => _buffer.IsFull;

        /// <summary>
        /// Writes audio samples to the buffer.
        /// </summary>
        /// <param name="samples">Array of audio samples.</param>
        /// <param name="offset">Offset in the array to start reading from.</param>
        /// <param name="count">Number of samples to write.</param>
        /// <returns>Number of samples actually written.</returns>
        public int WriteSamples(float[] samples, int offset = 0, int count = -1)
        {
            return _buffer.Write(samples, offset, count);
        }

        /// <summary>
        /// Reads audio samples from the buffer.
        /// </summary>
        /// <param name="samples">Array to read samples into.</param>
        /// <param name="offset">Offset in the array to start writing to.</param>
        /// <param name="count">Maximum number of samples to read.</param>
        /// <returns>Number of samples actually read.</returns>
        public int ReadSamples(float[] samples, int offset = 0, int count = -1)
        {
            return _buffer.Read(samples, offset, count);
        }

        /// <summary>
        /// Reads samples and returns them in a new array. 
        /// Uses internal temp buffer for efficiency.
        /// </summary>
        /// <param name="maxSamples">Maximum number of samples to read.</param>
        /// <returns>Array containing the read samples.</returns>
        public float[] ReadSamples(int maxSamples)
        {
            int samplesToRead = Math.Min(maxSamples, AvailableSamples);
            if (samplesToRead <= 0)
                return Array.Empty<float>();

            var result = new float[samplesToRead];
            int actualRead = _buffer.Read(result);
            
            if (actualRead < samplesToRead)
            {
                Array.Resize(ref result, actualRead);
            }

            return result;
        }

        /// <summary>
        /// Peeks at samples without consuming them.
        /// </summary>
        /// <param name="samples">Array to copy peeked samples into.</param>
        /// <param name="offset">Offset in the array to start writing to.</param>
        /// <param name="count">Maximum number of samples to peek.</param>
        /// <returns>Number of samples actually peeked.</returns>
        public unsafe int PeekSamples(float[] samples, int offset = 0, int count = -1)
        {
            if (samples == null)
                throw new ArgumentNullException(nameof(samples));

            if (count == -1)
                count = samples.Length - offset;

            fixed (float* samplesPtr = &samples[offset])
            {
                return _buffer.Peek(samplesPtr, count);
            }
        }

        /// <summary>
        /// Skips the specified number of samples.
        /// </summary>
        /// <param name="sampleCount">Number of samples to skip.</param>
        /// <returns>Number of samples actually skipped.</returns>
        public int SkipSamples(int sampleCount)
        {
            return _buffer.Skip(sampleCount);
        }

        /// <summary>
        /// Clears all samples from the buffer.
        /// </summary>
        public void Clear()
        {
            _buffer.Clear();
        }

        /// <summary>
        /// Disposes the buffer and releases all resources.
        /// </summary>
        public void Dispose()
        {
            _buffer?.Dispose();
        }
    }

    /// <summary>
    /// Factory for creating optimally-sized circular buffers for audio processing.
    /// </summary>
    public static class CircularBufferFactory
    {
        /// <summary>
        /// Creates an audio circular buffer optimized for the specified latency requirements.
        /// </summary>
        /// <param name="sampleRate">Audio sample rate (e.g., 44100).</param>
        /// <param name="targetLatencyMs">Target latency in milliseconds.</param>
        /// <param name="safetyMargin">Safety margin multiplier (default: 2.0).</param>
        /// <returns>Optimized audio circular buffer.</returns>
        public static AudioCircularBuffer CreateForLatency(int sampleRate, double targetLatencyMs, double safetyMargin = 2.0)
        {
            double samplesPerMs = sampleRate / 1000.0;
            int requiredSamples = (int)Math.Ceiling(targetLatencyMs * samplesPerMs * safetyMargin);
            int capacity = NextPowerOfTwo(Math.Max(requiredSamples, 1024));

            return new AudioCircularBuffer(capacity);
        }

        /// <summary>
        /// Creates an audio circular buffer optimized for FFT processing.
        /// </summary>
        /// <param name="fftSize">FFT size (e.g., 1024).</param>
        /// <param name="overlapFactor">Overlap factor for windowing (default: 2.0).</param>
        /// <param name="bufferCount">Number of FFT buffers to accommodate (default: 4).</param>
        /// <returns>Optimized audio circular buffer.</returns>
        public static AudioCircularBuffer CreateForFFT(int fftSize, double overlapFactor = 2.0, int bufferCount = 4)
        {
            int requiredSamples = (int)Math.Ceiling(fftSize * overlapFactor * bufferCount);
            int capacity = NextPowerOfTwo(requiredSamples);

            return new AudioCircularBuffer(capacity);
        }

        /// <summary>
        /// Finds the next power of 2 greater than or equal to the specified value.
        /// </summary>
        /// <param name="value">Input value.</param>
        /// <returns>Next power of 2.</returns>
        private static int NextPowerOfTwo(int value)
        {
            if (value <= 0) return 1;

            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value++;

            return value;
        }
    }
}