using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.SystemTray.ContextMenu
{
    /// <summary>
    /// Windows 11 styled context menu manager for system tray integration.
    /// Provides modern, accessible context menus with theme support.
    /// </summary>
    public sealed class ContextMenuManager : IContextMenuManager
    {
        private readonly ILogger<ContextMenuManager> _logger;
        private readonly object _menuLock = new();
        
        private ContextMenuStrip? _contextMenu;
        private readonly List<IContextMenuItem> _menuItems = new();
        private VisualizationTheme? _currentTheme;
        
        private bool _isVisible;
        private bool _isInitialized;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the ContextMenuManager.
        /// </summary>
        /// <param name="logger">Logger for diagnostic information.</param>
        public ContextMenuManager(ILogger<ContextMenuManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogDebug("ContextMenuManager created");
        }

        #region Events

        /// <inheritdoc />
        public event EventHandler<MenuItemClickedEventArgs>? MenuItemClicked;

        /// <inheritdoc />
        public event EventHandler<MenuShowingEventArgs>? MenuShowing;

        /// <inheritdoc />
        public event EventHandler<MenuClosedEventArgs>? MenuClosed;

        #endregion

        #region Properties

        /// <inheritdoc />
        public bool IsVisible => _isVisible;

        /// <inheritdoc />
        public IReadOnlyList<IContextMenuItem> MenuItems => _menuItems.AsReadOnly();

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ContextMenuManager));

            if (_isInitialized)
            {
                _logger.LogWarning("ContextMenuManager is already initialized");
                return Task.CompletedTask;
            }

            _logger.LogInformation("Initializing context menu manager");

            try
            {
                lock (_menuLock)
                {
                    CreateContextMenu();
                    SetupDefaultMenuItems();
                    _isInitialized = true;
                }

                _logger.LogDebug("ContextMenuManager initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize ContextMenuManager");
                throw;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task ShowMenuAsync(Point location, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ContextMenuManager));

            if (!_isInitialized)
                throw new InvalidOperationException("ContextMenuManager must be initialized before showing menu");

            try
            {
                // Fire showing event
                var showingArgs = new MenuShowingEventArgs(location);
                MenuShowing?.Invoke(this, showingArgs);

                if (showingArgs.Cancel)
                {
                    _logger.LogDebug("Menu show cancelled by event handler");
                    return Task.CompletedTask;
                }

                lock (_menuLock)
                {
                    if (_contextMenu != null)
                    {
                        _contextMenu.Show(location);
                        _isVisible = true;
                        _logger.LogDebug("Context menu shown at {Location}", location);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show context menu");
                throw;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task HideMenuAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ContextMenuManager));

            try
            {
                lock (_menuLock)
                {
                    if (_contextMenu != null && _isVisible)
                    {
                        _contextMenu.Hide();
                        _isVisible = false;
                        _logger.LogDebug("Context menu hidden");

                        // Fire closed event
                        var closedArgs = new MenuClosedEventArgs(MenuCloseReason.ApplicationRequest);
                        MenuClosed?.Invoke(this, closedArgs);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to hide context menu");
                throw;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task AddMenuItemAsync(IContextMenuItem menuItem, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ContextMenuManager));

            if (menuItem == null)
                throw new ArgumentNullException(nameof(menuItem));

            try
            {
                lock (_menuLock)
                {
                    // Check if item already exists
                    if (_menuItems.Any(item => item.Id == menuItem.Id))
                    {
                        _logger.LogWarning("Menu item with ID {Id} already exists", menuItem.Id);
                        return Task.CompletedTask;
                    }

                    _menuItems.Add(menuItem);
                    
                    if (_contextMenu != null)
                    {
                        AddMenuItemToStrip(menuItem, _contextMenu.Items);
                    }

                    _logger.LogDebug("Added menu item: {Id} - {Text}", menuItem.Id, menuItem.Text);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add menu item {Id}", menuItem.Id);
                throw;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RemoveMenuItemAsync(string itemId, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ContextMenuManager));

            if (string.IsNullOrEmpty(itemId))
                throw new ArgumentException("Item ID cannot be null or empty", nameof(itemId));

            try
            {
                lock (_menuLock)
                {
                    var menuItem = _menuItems.FirstOrDefault(item => item.Id == itemId);
                    if (menuItem != null)
                    {
                        _menuItems.Remove(menuItem);
                        
                        if (_contextMenu != null)
                        {
                            RemoveMenuItemFromStrip(itemId, _contextMenu.Items);
                        }

                        _logger.LogDebug("Removed menu item: {Id}", itemId);
                    }
                    else
                    {
                        _logger.LogWarning("Menu item with ID {Id} not found for removal", itemId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove menu item {Id}", itemId);
                throw;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task UpdateMenuItemAsync(IContextMenuItem menuItem, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ContextMenuManager));

            if (menuItem == null)
                throw new ArgumentNullException(nameof(menuItem));

            try
            {
                lock (_menuLock)
                {
                    var existingIndex = _menuItems.FindIndex(item => item.Id == menuItem.Id);
                    if (existingIndex >= 0)
                    {
                        _menuItems[existingIndex] = menuItem;
                        
                        if (_contextMenu != null)
                        {
                            RemoveMenuItemFromStrip(menuItem.Id, _contextMenu.Items);
                            AddMenuItemToStrip(menuItem, _contextMenu.Items);
                        }

                        _logger.LogDebug("Updated menu item: {Id} - {Text}", menuItem.Id, menuItem.Text);
                    }
                    else
                    {
                        _logger.LogWarning("Menu item with ID {Id} not found for update", menuItem.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update menu item {Id}", menuItem.Id);
                throw;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SetMenuItemEnabledAsync(string itemId, bool enabled, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ContextMenuManager));

            try
            {
                lock (_menuLock)
                {
                    var menuItem = _menuItems.FirstOrDefault(item => item.Id == itemId);
                    if (menuItem != null)
                    {
                        menuItem.Enabled = enabled;
                        
                        if (_contextMenu != null)
                        {
                            var toolStripItem = FindToolStripItem(_contextMenu.Items, itemId);
                            if (toolStripItem != null)
                            {
                                toolStripItem.Enabled = enabled;
                            }
                        }

                        _logger.LogDebug("Set menu item {Id} enabled: {Enabled}", itemId, enabled);
                    }
                    else
                    {
                        _logger.LogWarning("Menu item with ID {Id} not found for enable/disable", itemId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set menu item {Id} enabled state", itemId);
                throw;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SetMenuItemCheckedAsync(string itemId, bool @checked, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ContextMenuManager));

            try
            {
                lock (_menuLock)
                {
                    var menuItem = _menuItems.FirstOrDefault(item => item.Id == itemId);
                    if (menuItem != null)
                    {
                        menuItem.Checked = @checked;
                        
                        if (_contextMenu != null)
                        {
                            var toolStripItem = FindToolStripItem(_contextMenu.Items, itemId);
                            if (toolStripItem is ToolStripMenuItem menuStripItem)
                            {
                                menuStripItem.Checked = @checked;
                            }
                        }

                        _logger.LogDebug("Set menu item {Id} checked: {Checked}", itemId, @checked);
                    }
                    else
                    {
                        _logger.LogWarning("Menu item with ID {Id} not found for check/uncheck", itemId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set menu item {Id} checked state", itemId);
                throw;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task ApplyThemeAsync(VisualizationTheme theme, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ContextMenuManager));

            if (theme == null)
                throw new ArgumentNullException(nameof(theme));

            try
            {
                lock (_menuLock)
                {
                    _currentTheme = theme;
                    
                    if (_contextMenu != null)
                    {
                        ApplyThemeToContextMenu(_contextMenu, theme);
                    }

                    _logger.LogDebug("Applied theme to context menu: {ThemeName}", theme.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply theme to context menu");
                throw;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RebuildMenuAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ContextMenuManager));

            try
            {
                lock (_menuLock)
                {
                    if (_contextMenu != null)
                    {
                        _contextMenu.Items.Clear();
                        
                        foreach (var menuItem in _menuItems)
                        {
                            AddMenuItemToStrip(menuItem, _contextMenu.Items);
                        }

                        if (_currentTheme != null)
                        {
                            ApplyThemeToContextMenu(_contextMenu, _currentTheme);
                        }

                        _logger.LogDebug("Context menu rebuilt with {Count} items", _menuItems.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rebuild context menu");
                throw;
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Private Methods

        private void CreateContextMenu()
        {
            _contextMenu = new ContextMenuStrip
            {
                ShowImageMargin = true,
                ShowCheckMargin = true,
                AutoSize = true,
                RenderMode = ToolStripRenderMode.Professional
            };

            // Wire up events
            _contextMenu.Opening += OnContextMenuOpening;
            _contextMenu.Closed += OnContextMenuClosed;
            _contextMenu.ItemClicked += OnContextMenuItemClicked;

            _logger.LogDebug("Context menu created");
        }

        private void SetupDefaultMenuItems()
        {
            // Add default menu items
            var settingsItem = ContextMenuItem.CreateWithIcon(
                "settings", 
                "Settings...", 
                SystemIcons.Application.ToBitmap(),
                item => OnSettingsClicked(item)
            ).WithShortcut(Keys.Control | Keys.S);

            var separatorItem = ContextMenuItem.CreateSeparator("separator1");

            var aboutItem = ContextMenuItem.CreateStandard(
                "about", 
                "About TaskbarEqualizer", 
                item => OnAboutClicked(item)
            );

            var exitItem = ContextMenuItem.CreateStandard(
                "exit", 
                "Exit", 
                item => OnExitClicked(item)
            ).WithShortcut(Keys.Alt | Keys.F4);

            _menuItems.AddRange(new[] { settingsItem, separatorItem, aboutItem, exitItem });

            // Add items to the menu strip
            if (_contextMenu != null)
            {
                foreach (var menuItem in _menuItems)
                {
                    AddMenuItemToStrip(menuItem, _contextMenu.Items);
                }
            }
        }

        private void AddMenuItemToStrip(IContextMenuItem menuItem, ToolStripItemCollection items)
        {
            ToolStripItem toolStripItem;

            switch (menuItem.ItemType)
            {
                case ContextMenuItemType.Separator:
                    toolStripItem = new ToolStripSeparator
                    {
                        Name = menuItem.Id,
                        Visible = menuItem.Visible
                    };
                    break;

                case ContextMenuItemType.Submenu:
                    var submenu = new ToolStripMenuItem(menuItem.Text)
                    {
                        Name = menuItem.Id,
                        Enabled = menuItem.Enabled,
                        Visible = menuItem.Visible,
                        Image = menuItem.Icon,
                        Tag = menuItem
                    };

                    // Add subitems
                    foreach (var subItem in menuItem.SubItems)
                    {
                        AddMenuItemToStrip(subItem, submenu.DropDownItems);
                    }

                    toolStripItem = submenu;
                    break;

                default:
                    var menuStripItem = new ToolStripMenuItem(menuItem.Text)
                    {
                        Name = menuItem.Id,
                        Enabled = menuItem.Enabled,
                        Visible = menuItem.Visible,
                        Checked = menuItem.Checked,
                        CheckOnClick = menuItem.ItemType == ContextMenuItemType.Checkbox ||
                                      menuItem.ItemType == ContextMenuItemType.RadioButton,
                        Image = menuItem.Icon,
                        ShortcutKeys = menuItem.Shortcut,
                        Tag = menuItem
                    };

                    toolStripItem = menuStripItem;
                    break;
            }

            items.Add(toolStripItem);
        }

        private void RemoveMenuItemFromStrip(string itemId, ToolStripItemCollection items)
        {
            var itemToRemove = items.Cast<ToolStripItem>().FirstOrDefault(item => item.Name == itemId);
            if (itemToRemove != null)
            {
                items.Remove(itemToRemove);
                itemToRemove.Dispose();
            }
        }

        private ToolStripItem? FindToolStripItem(ToolStripItemCollection items, string itemId)
        {
            foreach (ToolStripItem item in items)
            {
                if (item.Name == itemId)
                    return item;

                if (item is ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
                {
                    var found = FindToolStripItem(menuItem.DropDownItems, itemId);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        private void ApplyThemeToContextMenu(ContextMenuStrip contextMenu, VisualizationTheme theme)
        {
            // Apply Windows 11 styling based on theme
            var colorScheme = theme.DarkMode; // Default to dark mode for now
            
            contextMenu.BackColor = colorScheme.BackgroundColor != Color.Transparent 
                ? colorScheme.BackgroundColor 
                : SystemColors.Control;
            
            contextMenu.ForeColor = colorScheme.PrimaryColor;

            // Apply styling to all items
            ApplyThemeToItems(contextMenu.Items, colorScheme);
        }

        private void ApplyThemeToItems(ToolStripItemCollection items, ColorScheme colorScheme)
        {
            foreach (ToolStripItem item in items)
            {
                item.BackColor = colorScheme.BackgroundColor != Color.Transparent 
                    ? colorScheme.BackgroundColor 
                    : SystemColors.Control;
                
                item.ForeColor = colorScheme.PrimaryColor;

                if (item is ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
                {
                    ApplyThemeToItems(menuItem.DropDownItems, colorScheme);
                }
            }
        }

        #endregion

        #region Event Handlers

        private void OnContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _isVisible = true;
            _logger.LogDebug("Context menu opening");
        }

        private void OnContextMenuClosed(object? sender, ToolStripDropDownClosedEventArgs e)
        {
            _isVisible = false;
            
            var reason = e.CloseReason switch
            {
                ToolStripDropDownCloseReason.ItemClicked => MenuCloseReason.ItemSelected,
                ToolStripDropDownCloseReason.Keyboard => MenuCloseReason.EscapePressed,
                ToolStripDropDownCloseReason.AppClicked => MenuCloseReason.ApplicationRequest,
                ToolStripDropDownCloseReason.AppFocusChange => MenuCloseReason.ClickedOutside,
                _ => MenuCloseReason.SystemRequest
            };

            var closedArgs = new MenuClosedEventArgs(reason, reason == MenuCloseReason.ItemSelected);
            MenuClosed?.Invoke(this, closedArgs);

            _logger.LogDebug("Context menu closed: {Reason}", reason);
        }

        private void OnContextMenuItemClicked(object? sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem?.Tag is IContextMenuItem menuItem)
            {
                var clickedArgs = new MenuItemClickedEventArgs(
                    menuItem, 
                    MouseButtons.Left, 
                    Cursor.Position
                );

                MenuItemClicked?.Invoke(this, clickedArgs);

                // Execute the click action
                menuItem.ClickAction?.Invoke(menuItem);

                _logger.LogDebug("Menu item clicked: {Id} - {Text}", menuItem.Id, menuItem.Text);
            }
        }

        private void OnSettingsClicked(IContextMenuItem menuItem)
        {
            _logger.LogInformation("Settings menu item clicked");
            // Settings dialog will be implemented later
        }

        private void OnAboutClicked(IContextMenuItem menuItem)
        {
            _logger.LogInformation("About menu item clicked");
            
            MessageBox.Show(
                "TaskbarEqualizer v1.0\nReal-time audio visualization for Windows 11 taskbar\n\n© 2024 TaskbarEqualizer",
                "About TaskbarEqualizer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void OnExitClicked(IContextMenuItem menuItem)
        {
            _logger.LogInformation("Exit menu item clicked");
            Application.Exit();
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the context menu manager and releases resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                lock (_menuLock)
                {
                    if (_contextMenu != null)
                    {
                        _contextMenu.Opening -= OnContextMenuOpening;
                        _contextMenu.Closed -= OnContextMenuClosed;
                        _contextMenu.ItemClicked -= OnContextMenuItemClicked;
                        
                        _contextMenu.Dispose();
                        _contextMenu = null;
                    }

                    _menuItems.Clear();
                }

                _logger.LogDebug("ContextMenuManager disposed");
            }
        }

        #endregion
    }
}