using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class HotkeyController
    {
        private readonly MainForm _owner;
        private BrowserState State => _owner.State;

        private HotkeyBindingMap _bindings = new();

        public HotkeyController(MainForm owner)
        {
            _owner = owner;
        }

        public void Reload()
        {
            _bindings = new HotkeyBindingMap();
            _bindings.Load(AppSettings.Current.Hotkeys);
        }

        public bool TryGetBinding(string action, out Keys keys)
        {
            return _bindings.TryGetBinding(action, out keys);
        }

        public bool IsActionKeyCode(string action, Keys keyCode)
        {
            return _bindings.IsActionKeyCode(action, keyCode);
        }

        public bool IsActionKeyData(string action, Keys keyData)
        {
            return _bindings.IsActionKeyData(action, keyData);
        }

        public bool HandleProcessCmdKey(ref Message msg, Keys keyData)
        {
            // WinForms sometimes drops Alt in ProcessCmdKey. Prefer the real-time modifier state.
            var effective = NormalizeKeyData(keyData);

            // Block destructive shortcuts in This PC (drives view), regardless of focus.
            if (State.CurrentPath == ThisPcPath && !_owner.IsSearchMode)
            {
                if (effective == (Keys.Control | Keys.X) ||
                    effective == (Keys.Control | Keys.V) ||
                    effective == Keys.Delete ||
                    effective == (Keys.Shift | Keys.Delete))
                {
                    return true;
                }
            }

            // Tab switching and tab closing are configurable and must work even while typing.
            if (TryMapAction(effective, out var earlyAction) &&
                (earlyAction.StartsWith("SwitchTab", StringComparison.OrdinalIgnoreCase) ||
                 earlyAction.Equals("CloseTab", StringComparison.OrdinalIgnoreCase)))
            {
                ExecuteAction(earlyAction);
                return true;
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
                // Image-viewer-only bindings are read by ImageViewerForm itself. Do not
                // let the main form consume those keys while it has focus.
                if (IsImageViewerOnlyAction(action) || IsListViewOnlyAction(action))
                    return false;

                bool isFocusOrGlobal =
                    action.StartsWith("Focus", StringComparison.OrdinalIgnoreCase) ||
                    action.StartsWith("Nav", StringComparison.OrdinalIgnoreCase) ||
                    action is "OpenSettings" or "CloseApp" or "ToggleFullscreen" or "Refresh" ||
                    action is "NewTab" or "NextTab" or "PrevTab" or "CloseTab" ||
                    action.StartsWith("SwitchTab", StringComparison.OrdinalIgnoreCase);

                if (inInput && !isFocusOrGlobal)
                    return false; // Let the focused input handle it.

                ExecuteAction(action);
                return true;
            }

            if (inInput)
                return false;

            return false;
        }

        public void ExecuteAction(string action)
        {
            if (action.StartsWith("SwitchTab", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(action["SwitchTab".Length..], out var tabNumber))
            {
                int index = tabNumber - 1;
                if (index >= 0 && index < _owner._tabsController.Count)
                    _owner.SwitchToTab(index);
                return;
            }

            switch (action)
            {
                case "NavBack": _owner.GoBack(); break;
                case "NavForward": _owner.GoForward(); break;
                case "FocusAddress": _owner.EnableAddressEdit(); break;
                case "FocusSearch": _owner.FocusSearchBox(tagOnly: false); break;
                case "FocusTagSearch": _owner.FocusSearchBox(tagOnly: true); break;
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
            if (_owner._sidebar.Focused || State.CurrentPath == ThisPcPath) return;
                    _owner.Paste();
                    break;
                case "Delete": if (_owner.CanManipulateSelected()) _owner.DeleteSelected(permanent: false); break;
                case "DeletePermanent": if (_owner.CanManipulateSelected()) _owner.DeleteSelected(permanent: true); break;
                case "Rename": if (_owner.CanManipulateSelected()) _owner.StartRename(); break;
            case "EditTags": if (_owner.CanManipulateSelected() && State.CurrentPath != ThisPcPath) _owner.EditTags(); break;
                case "SelectAll": _owner.SelectAll(); break;

                case "FocusFilePanel":
                    _owner._listView.Focus();
                    if (_owner._listView.Items.Count > 0 && _owner._listView.SelectedIndices.Count == 0)
                    {
                        _owner._listView.SelectedIndices.Add(0);
                        try { _owner._listView.Items[0].EnsureVisible(); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
                    }
                    break;
                case "FocusAI":
                    _owner._llmChatPanel?.FocusInput();
                    break;

                case "Undo":
            if (State.CurrentPath == ThisPcPath) return;
                    _owner.Undo();
                    break;
                case "Redo":
            if (State.CurrentPath == ThisPcPath) return;
                    _owner.Redo();
                    break;
                case "ToggleSidebar":
                    _owner.ToggleSidebar();
                    break;

                // Tab actions (configurable in settings).
                case "NewTab":
                    _owner.AddNewTab();
                    break;
                case "CloseTab":
                    _owner.CloseTab(_owner._tabsController.ActiveIndex);
                    break;
                case "NextTab":
                    if (_owner._tabsController.Count > 0)
                        _owner.SwitchToTab((_owner._tabsController.ActiveIndex + 1) % _owner._tabsController.Count);
                    break;
                case "PrevTab":
                    if (_owner._tabsController.Count > 0)
                        _owner.SwitchToTab((_owner._tabsController.ActiveIndex - 1 + _owner._tabsController.Count) % _owner._tabsController.Count);
                    break;
                case "ClearSelection":
                    _owner._listView.SelectedIndices.Clear();
                    break;
                case "ZoomIconsIn":
                case "ZoomIconsInNumpad":
                    _owner._iconZoomController.HandleZoomHotkey(1);
                    break;
                case "ZoomIconsOut":
                case "ZoomIconsOutNumpad":
                    _owner._iconZoomController.HandleZoomHotkey(-1);
                    break;
            }
        }

        private static bool IsImageViewerOnlyAction(string action)
        {
            return action.StartsWith("ImageViewer", StringComparison.OrdinalIgnoreCase) ||
                action is "ToggleOcrBoxes" or "ToggleSavedTranslation" or "FitSmallDimension";
        }

        private static bool IsListViewOnlyAction(string action)
            => action is "OpenSelected" or "NavigateUp";

        private bool TryMapAction(Keys effectiveKeyData, out string action)
        {
            return _bindings.TryGetAction(effectiveKeyData, out action!);
        }

        private static Keys NormalizeKeyData(Keys keyData)
        {
            var code = keyData & Keys.KeyCode;
            var mods = Control.ModifierKeys & (Keys.Control | Keys.Shift | Keys.Alt);
            return code | mods;
        }

    }
}
