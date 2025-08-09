using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TaskbarEqualizer.SystemTray.Interfaces
{
    /// <summary>
    /// Interface for managing context menu functionality with Windows 11 styling.
    /// Provides modern, accessible context menus for system tray interaction.
    /// </summary>
    public interface IContextMenuManager : IDisposable
    {
        /// <summary>
        /// Event fired when a menu item is clicked.
        /// </summary>
        event EventHandler<MenuItemClickedEventArgs> MenuItemClicked;

        /// <summary>
        /// Event fired when the menu is about to be shown.
        /// </summary>
        event EventHandler<MenuShowingEventArgs> MenuShowing;

        /// <summary>
        /// Event fired when the menu is closed.
        /// </summary>
        event EventHandler<MenuClosedEventArgs> MenuClosed;

        /// <summary>
        /// Gets a value indicating whether the context menu is currently visible.
        /// </summary>
        bool IsVisible { get; }

        /// <summary>
        /// Gets the current menu items.
        /// </summary>
        IReadOnlyList<IContextMenuItem> MenuItems { get; }

        /// <summary>
        /// Gets the underlying ContextMenuStrip for direct assignment to controls.
        /// </summary>
        ContextMenuStrip? ContextMenuStrip { get; }

        /// <summary>
        /// Initializes the context menu manager.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous initialization.</returns>
        Task InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Shows the context menu at the specified location.
        /// </summary>
        /// <param name="location">Screen coordinates where to show the menu.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous show operation.</returns>
        Task ShowMenuAsync(Point location, CancellationToken cancellationToken = default);

        /// <summary>
        /// Hides the context menu if it's currently visible.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous hide operation.</returns>
        Task HideMenuAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a menu item to the context menu.
        /// </summary>
        /// <param name="menuItem">Menu item to add.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous add operation.</returns>
        Task AddMenuItemAsync(IContextMenuItem menuItem, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a menu item from the context menu.
        /// </summary>
        /// <param name="itemId">ID of the menu item to remove.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous remove operation.</returns>
        Task RemoveMenuItemAsync(string itemId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates an existing menu item.
        /// </summary>
        /// <param name="menuItem">Updated menu item.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous update operation.</returns>
        Task UpdateMenuItemAsync(IContextMenuItem menuItem, CancellationToken cancellationToken = default);

        /// <summary>
        /// Enables or disables a menu item.
        /// </summary>
        /// <param name="itemId">ID of the menu item.</param>
        /// <param name="enabled">Whether the item should be enabled.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous enable operation.</returns>
        Task SetMenuItemEnabledAsync(string itemId, bool enabled, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the checked state of a menu item.
        /// </summary>
        /// <param name="itemId">ID of the menu item.</param>
        /// <param name="checked">Whether the item should be checked.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous check operation.</returns>
        Task SetMenuItemCheckedAsync(string itemId, bool @checked, CancellationToken cancellationToken = default);

        /// <summary>
        /// Applies Windows 11 theme to the context menu.
        /// </summary>
        /// <param name="theme">Theme configuration to apply.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous theme application.</returns>
        Task ApplyThemeAsync(VisualizationTheme theme, CancellationToken cancellationToken = default);

        /// <summary>
        /// Rebuilds the entire menu with current items.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous rebuild operation.</returns>
        Task RebuildMenuAsync(CancellationToken cancellationToken = default);
        Task ShowMenuAsync(object location);

    }

    /// <summary>
    /// Interface representing a context menu item.
    /// </summary>
    public interface IContextMenuItem
    {
        /// <summary>
        /// Unique identifier for the menu item.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Display text for the menu item.
        /// </summary>
        string Text { get; set; }

        /// <summary>
        /// Icon for the menu item.
        /// </summary>
        Image? Icon { get; set; }

        /// <summary>
        /// Keyboard shortcut for the menu item.
        /// </summary>
        Keys Shortcut { get; set; }

        /// <summary>
        /// Whether the menu item is enabled.
        /// </summary>
        bool Enabled { get; set; }

        /// <summary>
        /// Whether the menu item is checked.
        /// </summary>
        bool Checked { get; set; }

        /// <summary>
        /// Whether the menu item is visible.
        /// </summary>
        bool Visible { get; set; }

        /// <summary>
        /// Type of menu item.
        /// </summary>
        ContextMenuItemType ItemType { get; set; }

        /// <summary>
        /// Tag object for additional data.
        /// </summary>
        object? Tag { get; set; }

        /// <summary>
        /// Submenu items if this is a submenu.
        /// </summary>
        IList<IContextMenuItem> SubItems { get; }

        /// <summary>
        /// Action to execute when the item is clicked.
        /// </summary>
        Action<IContextMenuItem>? ClickAction { get; set; }
    }

    /// <summary>
    /// Event arguments for menu item click events.
    /// </summary>
    public class MenuItemClickedEventArgs : EventArgs
    {
        /// <summary>
        /// The menu item that was clicked.
        /// </summary>
        public IContextMenuItem MenuItem { get; }

        /// <summary>
        /// Mouse button used for the click.
        /// </summary>
        public MouseButtons Button { get; }

        /// <summary>
        /// Screen coordinates of the click.
        /// </summary>
        public Point ClickLocation { get; }

        public MenuItemClickedEventArgs(IContextMenuItem menuItem, MouseButtons button, Point clickLocation)
        {
            MenuItem = menuItem;
            Button = button;
            ClickLocation = clickLocation;
        }
    }

    /// <summary>
    /// Event arguments for menu showing events.
    /// </summary>
    public class MenuShowingEventArgs : EventArgs
    {
        /// <summary>
        /// Location where the menu will be shown.
        /// </summary>
        public Point Location { get; }

        /// <summary>
        /// Whether the show operation can be cancelled.
        /// </summary>
        public bool Cancel { get; set; }

        public MenuShowingEventArgs(Point location)
        {
            Location = location;
            Cancel = false;
        }
    }

    /// <summary>
    /// Event arguments for menu closed events.
    /// </summary>
    public class MenuClosedEventArgs : EventArgs
    {
        /// <summary>
        /// Reason why the menu was closed.
        /// </summary>
        public MenuCloseReason Reason { get; }

        /// <summary>
        /// Whether an item was selected before closing.
        /// </summary>
        public bool ItemSelected { get; }

        public MenuClosedEventArgs(MenuCloseReason reason, bool itemSelected = false)
        {
            Reason = reason;
            ItemSelected = itemSelected;
        }
    }

    /// <summary>
    /// Types of context menu items.
    /// </summary>
    public enum ContextMenuItemType
    {
        /// <summary>
        /// Standard menu item.
        /// </summary>
        Normal,

        /// <summary>
        /// Separator line.
        /// </summary>
        Separator,

        /// <summary>
        /// Submenu container.
        /// </summary>
        Submenu,

        /// <summary>
        /// Checkable menu item.
        /// </summary>
        Checkbox,

        /// <summary>
        /// Radio button menu item.
        /// </summary>
        RadioButton
    }

    /// <summary>
    /// Reasons for menu closure.
    /// </summary>
    public enum MenuCloseReason
    {
        /// <summary>
        /// User clicked outside the menu.
        /// </summary>
        ClickedOutside,

        /// <summary>
        /// User pressed escape key.
        /// </summary>
        EscapePressed,

        /// <summary>
        /// Menu item was selected.
        /// </summary>
        ItemSelected,

        /// <summary>
        /// Application requested closure.
        /// </summary>
        ApplicationRequest,

        /// <summary>
        /// System requested closure.
        /// </summary>
        SystemRequest
    }
}