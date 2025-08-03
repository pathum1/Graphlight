using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.SystemTray.ContextMenu
{
    /// <summary>
    /// Implementation of a context menu item with Windows 11 styling support.
    /// </summary>
    public class ContextMenuItem : IContextMenuItem
    {
        private string _text = string.Empty;
        private Image? _icon;
        private Keys _shortcut = Keys.None;
        private bool _enabled = true;
        private bool _checked = false;
        private bool _visible = true;
        private ContextMenuItemType _itemType = ContextMenuItemType.Normal;
        private object? _tag;
        private Action<IContextMenuItem>? _clickAction;

        /// <summary>
        /// Initializes a new instance of the ContextMenuItem class.
        /// </summary>
        /// <param name="id">Unique identifier for the menu item.</param>
        /// <param name="text">Display text for the menu item.</param>
        public ContextMenuItem(string id, string text)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            _text = text ?? throw new ArgumentNullException(nameof(text));
            SubItems = new List<IContextMenuItem>();
        }

        /// <summary>
        /// Initializes a new instance of the ContextMenuItem class.
        /// </summary>
        /// <param name="id">Unique identifier for the menu item.</param>
        /// <param name="text">Display text for the menu item.</param>
        /// <param name="clickAction">Action to execute when clicked.</param>
        public ContextMenuItem(string id, string text, Action<IContextMenuItem> clickAction)
            : this(id, text)
        {
            _clickAction = clickAction;
        }

        /// <summary>
        /// Initializes a new instance of the ContextMenuItem class.
        /// </summary>
        /// <param name="id">Unique identifier for the menu item.</param>
        /// <param name="text">Display text for the menu item.</param>
        /// <param name="icon">Icon for the menu item.</param>
        /// <param name="clickAction">Action to execute when clicked.</param>
        public ContextMenuItem(string id, string text, Image? icon, Action<IContextMenuItem> clickAction)
            : this(id, text, clickAction)
        {
            _icon = icon;
        }

        /// <summary>
        /// Initializes a new instance of the ContextMenuItem class.
        /// </summary>
        /// <param name="id">Unique identifier for the menu item.</param>
        /// <param name="text">Display text for the menu item.</param>
        /// <param name="icon">Icon for the menu item.</param>
        /// <param name="shortcut">Keyboard shortcut.</param>
        /// <param name="clickAction">Action to execute when clicked.</param>
        public ContextMenuItem(string id, string text, Image? icon, Keys shortcut, Action<IContextMenuItem> clickAction)
            : this(id, text, icon, clickAction)
        {
            _shortcut = shortcut;
        }

        #region IContextMenuItem Implementation

        /// <inheritdoc />
        public string Id { get; }

        /// <inheritdoc />
        public string Text
        {
            get => _text;
            set => _text = value ?? string.Empty;
        }

        /// <inheritdoc />
        public Image? Icon
        {
            get => _icon;
            set => _icon = value;
        }

        /// <inheritdoc />
        public Keys Shortcut
        {
            get => _shortcut;
            set => _shortcut = value;
        }

        /// <inheritdoc />
        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        /// <inheritdoc />
        public bool Checked
        {
            get => _checked;
            set => _checked = value;
        }

        /// <inheritdoc />
        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        /// <inheritdoc />
        public ContextMenuItemType ItemType
        {
            get => _itemType;
            set => _itemType = value;
        }

        /// <inheritdoc />
        public object? Tag
        {
            get => _tag;
            set => _tag = value;
        }

        /// <inheritdoc />
        public IList<IContextMenuItem> SubItems { get; }

        /// <inheritdoc />
        public Action<IContextMenuItem>? ClickAction
        {
            get => _clickAction;
            set => _clickAction = value;
        }

        #endregion

        #region Factory Methods

        /// <summary>
        /// Creates a standard menu item.
        /// </summary>
        /// <param name="id">Unique identifier.</param>
        /// <param name="text">Display text.</param>
        /// <param name="clickAction">Click action.</param>
        /// <returns>New context menu item.</returns>
        public static ContextMenuItem CreateStandard(string id, string text, Action<IContextMenuItem> clickAction)
        {
            return new ContextMenuItem(id, text, clickAction)
            {
                ItemType = ContextMenuItemType.Normal
            };
        }

        /// <summary>
        /// Creates a menu item with an icon.
        /// </summary>
        /// <param name="id">Unique identifier.</param>
        /// <param name="text">Display text.</param>
        /// <param name="icon">Menu icon.</param>
        /// <param name="clickAction">Click action.</param>
        /// <returns>New context menu item.</returns>
        public static ContextMenuItem CreateWithIcon(string id, string text, Image icon, Action<IContextMenuItem> clickAction)
        {
            return new ContextMenuItem(id, text, icon, clickAction)
            {
                ItemType = ContextMenuItemType.Normal
            };
        }

        /// <summary>
        /// Creates a separator menu item.
        /// </summary>
        /// <param name="id">Unique identifier.</param>
        /// <returns>New separator menu item.</returns>
        public static ContextMenuItem CreateSeparator(string id = "")
        {
            return new ContextMenuItem(string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id, "-")
            {
                ItemType = ContextMenuItemType.Separator,
                Enabled = false
            };
        }

        /// <summary>
        /// Creates a checkable menu item.
        /// </summary>
        /// <param name="id">Unique identifier.</param>
        /// <param name="text">Display text.</param>
        /// <param name="initialChecked">Initial checked state.</param>
        /// <param name="clickAction">Click action.</param>
        /// <returns>New checkable menu item.</returns>
        public static ContextMenuItem CreateCheckbox(string id, string text, bool initialChecked, Action<IContextMenuItem> clickAction)
        {
            return new ContextMenuItem(id, text, clickAction)
            {
                ItemType = ContextMenuItemType.Checkbox,
                Checked = initialChecked
            };
        }

        /// <summary>
        /// Creates a submenu item.
        /// </summary>
        /// <param name="id">Unique identifier.</param>
        /// <param name="text">Display text.</param>
        /// <param name="icon">Optional submenu icon.</param>
        /// <returns>New submenu item.</returns>
        public static ContextMenuItem CreateSubmenu(string id, string text, Image? icon = null)
        {
            return new ContextMenuItem(id, text)
            {
                ItemType = ContextMenuItemType.Submenu,
                Icon = icon
            };
        }

        /// <summary>
        /// Creates a radio button menu item.
        /// </summary>
        /// <param name="id">Unique identifier.</param>
        /// <param name="text">Display text.</param>
        /// <param name="initialChecked">Initial checked state.</param>
        /// <param name="clickAction">Click action.</param>
        /// <returns>New radio button menu item.</returns>
        public static ContextMenuItem CreateRadioButton(string id, string text, bool initialChecked, Action<IContextMenuItem> clickAction)
        {
            return new ContextMenuItem(id, text, clickAction)
            {
                ItemType = ContextMenuItemType.RadioButton,
                Checked = initialChecked
            };
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Adds a submenu item to this item.
        /// </summary>
        /// <param name="subItem">Submenu item to add.</param>
        /// <returns>This menu item for chaining.</returns>
        public ContextMenuItem AddSubItem(IContextMenuItem subItem)
        {
            if (subItem == null)
                throw new ArgumentNullException(nameof(subItem));

            if (ItemType != ContextMenuItemType.Submenu)
                ItemType = ContextMenuItemType.Submenu;

            SubItems.Add(subItem);
            return this;
        }

        /// <summary>
        /// Sets the keyboard shortcut for this menu item.
        /// </summary>
        /// <param name="shortcut">Keyboard shortcut keys.</param>
        /// <returns>This menu item for chaining.</returns>
        public ContextMenuItem WithShortcut(Keys shortcut)
        {
            Shortcut = shortcut;
            return this;
        }

        /// <summary>
        /// Sets the enabled state for this menu item.
        /// </summary>
        /// <param name="enabled">Whether the item should be enabled.</param>
        /// <returns>This menu item for chaining.</returns>
        public ContextMenuItem SetEnabled(bool enabled)
        {
            Enabled = enabled;
            return this;
        }

        /// <summary>
        /// Sets the checked state for this menu item.
        /// </summary>
        /// <param name="checked">Whether the item should be checked.</param>
        /// <returns>This menu item for chaining.</returns>
        public ContextMenuItem SetChecked(bool @checked)
        {
            Checked = @checked;
            return this;
        }

        /// <summary>
        /// Sets the visible state for this menu item.
        /// </summary>
        /// <param name="visible">Whether the item should be visible.</param>
        /// <returns>This menu item for chaining.</returns>
        public ContextMenuItem SetVisible(bool visible)
        {
            Visible = visible;
            return this;
        }

        /// <summary>
        /// Sets the tag object for this menu item.
        /// </summary>
        /// <param name="tag">Tag object to associate.</param>
        /// <returns>This menu item for chaining.</returns>
        public ContextMenuItem WithTag(object tag)
        {
            Tag = tag;
            return this;
        }

        #endregion

        #region Object Overrides

        /// <summary>
        /// Returns a string representation of this menu item.
        /// </summary>
        /// <returns>String representation.</returns>
        public override string ToString()
        {
            return $"{Id}: {Text} ({ItemType})";
        }

        /// <summary>
        /// Determines whether the specified object is equal to this menu item.
        /// </summary>
        /// <param name="obj">Object to compare.</param>
        /// <returns>True if equal.</returns>
        public override bool Equals(object? obj)
        {
            if (obj is ContextMenuItem other)
            {
                return Id.Equals(other.Id, StringComparison.Ordinal);
            }
            return false;
        }

        /// <summary>
        /// Returns a hash code for this menu item.
        /// </summary>
        /// <returns>Hash code.</returns>
        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        #endregion
    }
}