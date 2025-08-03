using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace TaskbarEqualizer.SystemTray.Interfaces
{
    /// <summary>
    /// Interface for Windows system tray integration and management.
    /// Provides taskbar icon display and user interaction capabilities.
    /// </summary>
    public interface ISystemTrayManager : INotifyPropertyChanged, IDisposable
    {
        /// <summary>
        /// Event fired when the tray icon is clicked.
        /// </summary>
        event EventHandler<TrayIconClickedEventArgs> TrayIconClicked;

        /// <summary>
        /// Event fired when the context menu is requested.
        /// </summary>
        event EventHandler<ContextMenuRequestedEventArgs> ContextMenuRequested;

        /// <summary>
        /// Event fired when the application should exit.
        /// </summary>
        event EventHandler<EventArgs> ExitRequested;

        /// <summary>
        /// Gets a value indicating whether the tray icon is currently visible.
        /// </summary>
        bool IsVisible { get; }

        /// <summary>
        /// Gets the current tray icon.
        /// </summary>
        Icon? CurrentIcon { get; }

        /// <summary>
        /// Gets the current tooltip text.
        /// </summary>
        string ToolTipText { get; }

        /// <summary>
        /// Gets a value indicating whether the manager is initialized.
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Initializes the system tray manager.
        /// </summary>
        /// <param name="initialIcon">Initial icon to display.</param>
        /// <param name="toolTipText">Initial tooltip text.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous initialization.</returns>
        Task InitializeAsync(Icon initialIcon, string toolTipText, CancellationToken cancellationToken = default);

        /// <summary>
        /// Shows the tray icon in the system tray.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous show operation.</returns>
        Task ShowAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Hides the tray icon from the system tray.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous hide operation.</returns>
        Task HideAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the tray icon with a new icon.
        /// </summary>
        /// <param name="newIcon">New icon to display.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous update operation.</returns>
        Task UpdateIconAsync(Icon newIcon, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the tray icon synchronously for high-performance scenarios.
        /// </summary>
        /// <param name="newIcon">New icon to display.</param>
        void UpdateIcon(Icon newIcon);

        /// <summary>
        /// Updates the tooltip text for the tray icon.
        /// </summary>
        /// <param name="text">New tooltip text.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous update operation.</returns>
        Task UpdateToolTipAsync(string text, CancellationToken cancellationToken = default);

        /// <summary>
        /// Shows a balloon tip notification.
        /// </summary>
        /// <param name="title">Notification title.</param>
        /// <param name="text">Notification text.</param>
        /// <param name="icon">Notification icon type.</param>
        /// <param name="timeout">Timeout in milliseconds.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous notification operation.</returns>
        Task ShowBalloonTipAsync(string title, string text, BalloonTipIcon icon = BalloonTipIcon.Info, 
            int timeout = 3000, CancellationToken cancellationToken = default);

        /// <summary>
        /// Enables or disables context menu functionality.
        /// </summary>
        /// <param name="enabled">Whether context menu should be enabled.</param>
        void SetContextMenuEnabled(bool enabled);

        /// <summary>
        /// Gets the current position of the tray icon on screen.
        /// </summary>
        /// <returns>Screen coordinates of the tray icon, or empty point if not available.</returns>
        Point GetTrayIconPosition();

        /// <summary>
        /// Forces the tray icon to refresh its appearance.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous refresh operation.</returns>
        Task RefreshAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Event arguments for tray icon click events.
    /// </summary>
    public class TrayIconClickedEventArgs : EventArgs
    {
        /// <summary>
        /// The mouse button that was clicked.
        /// </summary>
        public TrayMouseButton Button { get; }

        /// <summary>
        /// The number of clicks (single, double, etc.).
        /// </summary>
        public int ClickCount { get; }

        /// <summary>
        /// Screen coordinates where the click occurred.
        /// </summary>
        public Point ClickLocation { get; }

        /// <summary>
        /// Timestamp when the click occurred.
        /// </summary>
        public DateTime ClickTime { get; }

        public TrayIconClickedEventArgs(TrayMouseButton button, int clickCount, Point clickLocation)
        {
            Button = button;
            ClickCount = clickCount;
            ClickLocation = clickLocation;
            ClickTime = DateTime.Now;
        }
    }

    /// <summary>
    /// Event arguments for context menu request events.
    /// </summary>
    public class ContextMenuRequestedEventArgs : EventArgs
    {
        /// <summary>
        /// Screen coordinates where the context menu should appear.
        /// </summary>
        public Point MenuLocation { get; }

        /// <summary>
        /// Whether the request was triggered by keyboard navigation.
        /// </summary>
        public bool IsKeyboardTriggered { get; }

        public ContextMenuRequestedEventArgs(Point menuLocation, bool isKeyboardTriggered = false)
        {
            MenuLocation = menuLocation;
            IsKeyboardTriggered = isKeyboardTriggered;
        }
    }

    /// <summary>
    /// Mouse buttons for tray icon interactions.
    /// </summary>
    public enum TrayMouseButton
    {
        /// <summary>
        /// Left mouse button.
        /// </summary>
        Left,

        /// <summary>
        /// Right mouse button.
        /// </summary>
        Right,

        /// <summary>
        /// Middle mouse button.
        /// </summary>
        Middle
    }

    /// <summary>
    /// Balloon tip icon types.
    /// </summary>
    public enum BalloonTipIcon
    {
        /// <summary>
        /// No icon.
        /// </summary>
        None,

        /// <summary>
        /// Information icon.
        /// </summary>
        Info,

        /// <summary>
        /// Warning icon.
        /// </summary>
        Warning,

        /// <summary>
        /// Error icon.
        /// </summary>
        Error
    }
}