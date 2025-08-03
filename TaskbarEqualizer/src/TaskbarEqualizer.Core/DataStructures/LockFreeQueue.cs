using System;
using System.Threading;

namespace TaskbarEqualizer.Core.DataStructures
{
    /// <summary>
    /// High-performance lock-free queue implementation optimized for single-producer, single-consumer scenarios.
    /// Designed for real-time audio processing with minimal latency and zero lock contention.
    /// </summary>
    /// <typeparam name="T">Type of items stored in the queue.</typeparam>
    public sealed class LockFreeQueue<T> : IDisposable where T : class
    {
        private readonly T?[] _buffer;
        private readonly int _bufferMask;
        private volatile int _head;
        private volatile int _tail;
        private volatile bool _disposed;

        /// <summary>
        /// Initializes a new instance of the LockFreeQueue with the specified capacity.
        /// </summary>
        /// <param name="capacity">Capacity of the queue (must be a power of 2 for optimal performance).</param>
        /// <exception cref="ArgumentException">Thrown when capacity is not a power of 2.</exception>
        public LockFreeQueue(int capacity)
        {
            if (capacity <= 0 || (capacity & (capacity - 1)) != 0)
                throw new ArgumentException("Capacity must be a positive power of 2.", nameof(capacity));

            _buffer = new T[capacity];
            _bufferMask = capacity - 1;
            _head = 0;
            _tail = 0;
            _disposed = false;
        }

        /// <summary>
        /// Gets the maximum capacity of the queue.
        /// </summary>
        public int Capacity => _buffer.Length;

        /// <summary>
        /// Gets the approximate current count of items in the queue.
        /// Note: This is approximate due to concurrent access patterns.
        /// </summary>
        public int Count
        {
            get
            {
                int head = _head;
                int tail = _tail;
                return tail >= head ? tail - head : Capacity - head + tail;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the queue is empty.
        /// Note: This is a snapshot and may change immediately after the call returns.
        /// </summary>
        public bool IsEmpty => _head == _tail;

        /// <summary>
        /// Gets a value indicating whether the queue is full.
        /// Note: This is a snapshot and may change immediately after the call returns.
        /// </summary>
        public bool IsFull => (_tail + 1) % Capacity == _head;

        /// <summary>
        /// Attempts to enqueue an item to the queue.
        /// This method is optimized for single-producer scenarios.
        /// </summary>
        /// <param name="item">Item to enqueue.</param>
        /// <returns>True if the item was successfully enqueued; false if the queue is full.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the queue has been disposed.</exception>
        /// <exception cref="ArgumentNullException">Thrown when item is null.</exception>
        public bool TryEnqueue(T item)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LockFreeQueue<T>));

            if (item == null)
                throw new ArgumentNullException(nameof(item));

            int currentTail = _tail;
            int nextTail = (currentTail + 1) & _bufferMask;

            // Check if queue is full
            if (nextTail == _head)
                return false;

            // Store the item
            _buffer[currentTail] = item;

            // Memory barrier to ensure the item is written before updating the tail
            Thread.MemoryBarrier();

            // Update tail pointer
            _tail = nextTail;

            return true;
        }

        /// <summary>
        /// Attempts to dequeue an item from the queue.
        /// This method is optimized for single-consumer scenarios.
        /// </summary>
        /// <param name="item">The dequeued item, or default(T) if the queue is empty.</param>
        /// <returns>True if an item was successfully dequeued; false if the queue is empty.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the queue has been disposed.</exception>
        public bool TryDequeue(out T? item)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LockFreeQueue<T>));

            int currentHead = _head;

            // Check if queue is empty
            if (currentHead == _tail)
            {
                item = default(T);
                return false;
            }

            // Get the item
            item = _buffer[currentHead];

            // Clear the slot to prevent memory leaks
            _buffer[currentHead] = default(T);

            // Memory barrier to ensure the slot is cleared before updating the head
            Thread.MemoryBarrier();

            // Update head pointer
            _head = (currentHead + 1) & _bufferMask;

            return true;
        }

        /// <summary>
        /// Attempts to peek at the next item in the queue without removing it.
        /// </summary>
        /// <param name="item">The peeked item, or default(T) if the queue is empty.</param>
        /// <returns>True if an item was available to peek; false if the queue is empty.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the queue has been disposed.</exception>
        public bool TryPeek(out T? item)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LockFreeQueue<T>));

            int currentHead = _head;

            // Check if queue is empty
            if (currentHead == _tail)
            {
                item = default(T);
                return false;
            }

            // Get the item without removing it
            item = _buffer[currentHead];
            return true;
        }

        /// <summary>
        /// Clears all items from the queue.
        /// This operation is not atomic and should only be called when no other threads are accessing the queue.
        /// </summary>
        public void Clear()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LockFreeQueue<T>));

            // Clear all slots to prevent memory leaks
            for (int i = 0; i < _buffer.Length; i++)
            {
                _buffer[i] = default(T);
            }

            _head = 0;
            _tail = 0;
        }

        /// <summary>
        /// Disposes the queue and releases all resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Factory for creating optimally-sized lock-free queues based on usage patterns.
    /// </summary>
    public static class LockFreeQueueFactory
    {
        /// <summary>
        /// Creates a queue optimized for audio processing with low latency requirements.
        /// </summary>
        /// <typeparam name="T">Type of items to store.</typeparam>
        /// <param name="expectedLatencyMs">Expected processing latency in milliseconds.</param>
        /// <param name="sampleRate">Audio sample rate (e.g., 44100).</param>
        /// <returns>Optimally-sized lock-free queue.</returns>
        public static LockFreeQueue<T> CreateForAudioProcessing<T>(double expectedLatencyMs, int sampleRate) where T : class
        {
            // Calculate buffer size based on latency requirements
            // Add 50% safety margin and round up to next power of 2
            double samplesPerMs = sampleRate / 1000.0;
            int requiredSamples = (int)Math.Ceiling(expectedLatencyMs * samplesPerMs * 1.5);
            int capacity = NextPowerOfTwo(Math.Max(requiredSamples, 64));

            return new LockFreeQueue<T>(capacity);
        }

        /// <summary>
        /// Creates a queue optimized for high-frequency updates with burst tolerance.
        /// </summary>
        /// <typeparam name="T">Type of items to store.</typeparam>
        /// <param name="averageUpdatesPerSecond">Average number of updates per second.</param>
        /// <param name="burstMultiplier">Multiplier for handling burst scenarios (default: 3.0).</param>
        /// <returns>Optimally-sized lock-free queue.</returns>
        public static LockFreeQueue<T> CreateForHighFrequency<T>(int averageUpdatesPerSecond, double burstMultiplier = 3.0) where T : class
        {
            // Calculate capacity for 1 second of burst activity
            int requiredCapacity = (int)Math.Ceiling(averageUpdatesPerSecond * burstMultiplier);
            int capacity = NextPowerOfTwo(Math.Max(requiredCapacity, 32));

            return new LockFreeQueue<T>(capacity);
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