using System;
using System.Threading;
using System.Threading.Tasks;
using TaskbarEqualizer.Core.Interfaces;

namespace TaskbarEqualizer.SystemTray.Interfaces
{
    /// <summary>
    /// Interface for managing taskbar overlay visualization with Windows 11 compatibility.
    /// Provides real-time audio equalizer overlay directly on the Windows taskbar.
    /// </summary>
    public interface ITaskbarOverlayManager : IDisposable
    {
        /// <summary>
        /// Event fired when overlay status changes.
        /// </summary>
        event EventHandler<OverlayStatusChangedEventArgs> StatusChanged;

        /// <summary>
        /// Gets whether the overlay is currently active.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Gets the current overlay configuration.
        /// </summary>
        OverlayConfiguration Configuration { get; }

        /// <summary>
        /// Initializes the taskbar overlay system.
        /// </summary>
        /// <param name="configuration">Initial overlay configuration.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous initialization.</returns>
        Task InitializeAsync(OverlayConfiguration configuration, CancellationToken cancellationToken = default);

        /// <summary>
        /// Shows the taskbar overlay.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous show operation.</returns>
        Task ShowAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Hides the taskbar overlay.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous hide operation.</returns>
        Task HideAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the overlay with new spectrum data for visualization.
        /// </summary>
        /// <param name="spectrumData">Real-time frequency spectrum data.</param>
        void UpdateVisualization(SpectrumDataEventArgs spectrumData);

        /// <summary>
        /// Updates the overlay configuration.
        /// </summary>
        /// <param name="configuration">New overlay configuration.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous update.</returns>
        Task UpdateConfigurationAsync(OverlayConfiguration configuration, CancellationToken cancellationToken = default);

        /// <summary>
        /// Refreshes the taskbar position and adjusts overlay accordingly.
        /// </summary>
        Task RefreshTaskbarPositionAsync();
    }

    /// <summary>
    /// Event arguments for overlay status changes.
    /// </summary>
    public class OverlayStatusChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Previous overlay status.
        /// </summary>
        public OverlayStatus PreviousStatus { get; }

        /// <summary>
        /// Current overlay status.
        /// </summary>
        public OverlayStatus CurrentStatus { get; }

        /// <summary>
        /// Reason for the status change.
        /// </summary>
        public string Reason { get; }

        public OverlayStatusChangedEventArgs(OverlayStatus previousStatus, OverlayStatus currentStatus, string reason)
        {
            PreviousStatus = previousStatus;
            CurrentStatus = currentStatus;
            Reason = reason;
        }
    }

    /// <summary>
    /// Overlay status enumeration.
    /// </summary>
    public enum OverlayStatus
    {
        /// <summary>
        /// Overlay is not initialized.
        /// </summary>
        NotInitialized,

        /// <summary>
        /// Overlay is initialized but hidden.
        /// </summary>
        Hidden,

        /// <summary>
        /// Overlay is visible and active.
        /// </summary>
        Active,

        /// <summary>
        /// Overlay encountered an error.
        /// </summary>
        Error,

        /// <summary>
        /// Overlay is disposed.
        /// </summary>
        Disposed
    }

    /// <summary>
    /// Configuration for taskbar overlay display.
    /// </summary>
    public class OverlayConfiguration
    {
        /// <summary>
        /// Whether to enable the overlay.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Overlay opacity (0.0 to 1.0).
        /// </summary>
        public float Opacity { get; set; } = 0.8f;

        /// <summary>
        /// Position of the overlay on the taskbar.
        /// </summary>
        public OverlayPosition Position { get; set; } = OverlayPosition.Center;

        /// <summary>
        /// Width of the overlay in pixels.
        /// </summary>
        public int Width { get; set; } = 200;

        /// <summary>
        /// Height of the overlay in pixels.
        /// </summary>
        public int Height { get; set; } = 30;

        /// <summary>
        /// Margin from taskbar edges in pixels.
        /// </summary>
        public int Margin { get; set; } = 10;

        /// <summary>
        /// Update frequency in Hz.
        /// </summary>
        public int UpdateFrequency { get; set; } = 60;

        /// <summary>
        /// Whether to auto-hide when no audio is playing.
        /// </summary>
        public bool AutoHide { get; set; } = false;

        /// <summary>
        /// Auto-hide delay in milliseconds.
        /// </summary>
        public int AutoHideDelay { get; set; } = 5000;

        /// <summary>
        /// Rendering configuration for the visualization.
        /// </summary>
        public RenderConfiguration RenderConfiguration { get; set; } = new();
    }

    /// <summary>
    /// Position options for overlay placement on taskbar.
    /// </summary>
    public enum OverlayPosition
    {
        /// <summary>
        /// Left side of the taskbar.
        /// </summary>
        Left,

        /// <summary>
        /// Center of the taskbar.
        /// </summary>
        Center,

        /// <summary>
        /// Right side of the taskbar.
        /// </summary>
        Right,

        /// <summary>
        /// Custom position (user-defined coordinates).
        /// </summary>
        Custom
    }
}