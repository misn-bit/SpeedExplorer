using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class HotkeyController
    {
        private readonly MainForm _owner;

        private readonly Dictionary<string, Keys> _actionToKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Keys, string> _keysToAction = new();

        public HotkeyController(MainForm owner)
        {
            _owner = owner;
        }

        public void Reload()
        {
            _actionToKeys.Clear();
            _keysToAction.Clear();

            var shortcuts = AppSettings.Current.Hotkeys;
            foreach (var kvp in shortcuts)
            {
                try
                {
                    var converted = new KeysConverter().ConvertFromString(kvp.Value);
                    if (converted is not Keys parsed)
                        continue;
                    var normalized = NormalizeBinding(parsed);

                    _actionToKeys[kvp.Key] = normalized;

                    // If multiple actions share the same key, keep the first one (stable).
                    if (!_keysToAction.ContainsKey(normalized))
                        _keysToAction[normalized] = kvp.Key;
                }
                catch
                {
                    // Ignore malformed entries
                }
            }
        }

        public bool TryGetBinding(string action, out Keys keys)
        {
            return _actionToKeys.TryGetValue(action, out keys);
        }

        public bool IsActionKeyCode(string action, Keys keyCode)
        {
            if (!_actionToKeys.TryGetValue(action, out var keys)) return false;
            return (keys & Keys.KeyCode) == keyCode;
        }

        public bool HandleProcessCmdKey(ref Message msg, Keys keyData)
        {
            // WinForms sometimes drops Alt in ProcessCmdKey. Prefer the real-time modifier state.
            var effective = NormalizeKeyData(keyData);

            // Block destructive shortcuts in This PC (drives view), regardless of focus.
            if (_owner._currentPath == ThisPcPath && !_owner.IsSearchMode)
            {
                if (effective == (Keys.Control | Keys.X) ||
                    effective == (Keys.Control | Keys.V) ||
                    effective == Keys.Delete ||
                    effective == (Keys.Shift | Keys.Delete))
                {
                    return true;
                }
            }

            // Ctrl+1..9: direct tab switch (global).
            if ((effective & Keys.Control) == Keys.Control && (effective & (Keys.Alt | Keys.Shift)) == 0)
            {
                var code = effective & Keys.KeyCode;
                if (code >= Keys.D1 && code <= Keys.D9)
                {
                    int index = (int)(code - Keys.D1);
                    if (index >= 0 && index < _owner._tabsController.Count)
                    {
                        _owner.SwitchToTab(index);
                        return true;
                    }
                }

                // Ctrl+W: close tab (close window if last).
                if (effective == (Keys.Control | Keys.W))
                {
                    _owner.CloseTab(_owner._tabsController.ActiveIndex);
                    return true;
                }
            }

            bool inInput =
                _owner._searchBox.Focused ||
                _owner._addressBar.ContainsFocus ||
                (_owner._renameTextBox != null && _owner._renameTextBox.Focused) ||
                (_owner._llmChatPanel != null && _owner._llmChatPanel.IsInputFocused);

            // QuickLook is handled in ListView key handlers (Space down/up) so it can act like a "hold".
            if (TryGetBinding("QuickLook", out var quickLook) && effective == quickLook)
                return false;

            // Global/focus hotkeys should work even while typing.
            if (TryMapAction(effective, out var action))
            {
                bool isFocusOrGlobal =
                    action.StartsWith("Focus", StringComparison.OrdinalIgnoreCase) ||
                    action.StartsWith("Nav", StringComparison.OrdinalIgnoreCase) ||
                    action is "OpenSettings" or "CloseApp" or "ToggleFullscreen" or "Refresh" ||
                    action is "NewTab" or "NextTab" or "PrevTab";

                if (inInput && !isFocusOrGlobal)
                    return false; // Let the focused input handle it.

                ExecuteAction(action);
                return true;
            }

            if (inInput)
                return false;

            if (effective == Keys.Escape)
            {
                if (_owner._listView.SelectedIndices.Count > 0)
                {
                    _owner._listView.SelectedIndices.Clear();
                    return true;
                }
            }

            return false;
        }

        public void ExecuteAction(string action)
        {
            switch (action)
            {
                case "NavBack": _owner.GoBack(); break;
                case "NavForward": _owner.GoForward(); break;
                case "FocusAddress": _owner.EnableAddressEdit(); break;
                case "FocusSearch": _owner._searchBox.Focus(); _owner._searchBox.SelectAll(); break;
                case "FocusSidebar": _owner._sidebar.Focus(); break;
                case "Refresh": _ = _owner.RefreshCurrentAsync(); break;
                case "ShowProperties": _owner.ShowProperties(); break;
                case "OpenSettings": _owner.OpenSettings(); break;
                case "TogglePin": _owner.TogglePinSelected(); break;
                case "ToggleFullscreen": _owner.ToggleFullscreen(); break;
                case "CloseApp": _owner.Close(); break;

                case "Copy": if (_owner.CanManipulateSelected()) _owner.CopySelected(); break;
                case "Cut": if (_owner.CanManipulateSelected()) _owner.CutSelected(); break;
                case "Paste":
                    if (_owner._sidebar.Focused || _owner._currentPath == ThisPcPath) return;
                    _owner.Paste();
                    break;
                case "Delete": if (_owner.CanManipulateSelected()) _owner.DeleteSelected(permanent: false); break;
                case "DeletePermanent": if (_owner.CanManipulateSelected()) _owner.DeleteSelected(permanent: true); break;
                case "Rename": if (_owner.CanManipulateSelected()) _owner.StartRename(); break;
                case "EditTags": if (_owner.CanManipulateSelected() && _owner._currentPath != ThisPcPath) _owner.EditTags(); break;
                case "SelectAll": _owner.SelectAll(); break;

                case "FocusFilePanel":
                    _owner._listView.Focus();
                    if (_owner._listView.Items.Count > 0 && _owner._listView.SelectedIndices.Count == 0)
                    {
                        _owner._listView.SelectedIndices.Add(0);
                        try { _owner._listView.Items[0].EnsureVisible(); } catch { }
                    }
                    break;
                case "FocusAI":
                    _owner._llmChatPanel?.FocusInput();
                    break;

                case "Undo":
                    if (_owner._currentPath == ThisPcPath) return;
                    FileSystemService.PerformUndo();
                    _owner.RequestWatcherRefresh();
                    break;
                case "Redo":
                    if (_owner._currentPath == ThisPcPath) return;
                    FileSystemService.PerformRedo();
                    _owner.RequestWatcherRefresh();
                    break;
                case "ToggleSidebar":
                    _owner.ToggleSidebar();
                    break;

                // Tab actions (configurable in settings).
                case "NewTab":
                    _owner.AddNewTab();
                    break;
                case "NextTab":
                    if (_owner._tabsController.Count > 0)
                        _owner.SwitchToTab((_owner._tabsController.ActiveIndex + 1) % _owner._tabsController.Count);
                    break;
                case "PrevTab":
                    if (_owner._tabsController.Count > 0)
                        _owner.SwitchToTab((_owner._tabsController.ActiveIndex - 1 + _owner._tabsController.Count) % _owner._tabsController.Count);
                    break;
            }
        }

        private bool TryMapAction(Keys effectiveKeyData, out string action)
        {
            return _keysToAction.TryGetValue(effectiveKeyData, out action!);
        }

        private static Keys NormalizeKeyData(Keys keyData)
        {
            var code = keyData & Keys.KeyCode;
            var mods = Control.ModifierKeys & (Keys.Control | Keys.Shift | Keys.Alt);
            return code | mods;
        }

        private static Keys NormalizeBinding(Keys binding)
        {
            var code = binding & Keys.KeyCode;
            var mods = binding & (Keys.Control | Keys.Shift | Keys.Alt);
            return code | mods;
        }
    }
}
