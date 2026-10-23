using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    Form ISettingsLauncherHost.OwnerWindow => this;
    void ISettingsLauncherHost.ApplySettings(bool refreshCurrent, bool applyTheme) => ApplySettings(refreshCurrent, applyTheme);
    void ISettingsLauncherHost.ReloadHotkeys() => _hotkeyController.Reload();

    private sealed class SettingsLauncherController
    {
        private readonly ISettingsLauncherHost _owner;
        private SettingsForm? _form;

        public SettingsLauncherController(ISettingsLauncherHost owner)
        {
            _owner = owner;
        }

        public void OpenSettings()
        {
            if (_form is { IsDisposed: false })
            {
                _form.BringToFront();
                _form.Activate();
                return;
            }

            var form = new SettingsForm();
            _form = form;
            var ownerWindow = _owner.OwnerWindow;
            form.StartPosition = FormStartPosition.Manual;
            void UpdateSettingsBounds(object? sender = null, EventArgs? e = null)
            {
                if (!ownerWindow.IsDisposed && ownerWindow.IsHandleCreated && !form.IsDisposed)
                    form.SetOwnerClientBounds(ownerWindow.RectangleToScreen(ownerWindow.ClientRectangle));
            }

            UpdateSettingsBounds();
            ownerWindow.LocationChanged += UpdateSettingsBounds;
            ownerWindow.ClientSizeChanged += UpdateSettingsBounds;
            ownerWindow.SizeChanged += UpdateSettingsBounds;
            var resizeFilter = new SettingsResizeMessageFilter(form);
            var ownerClickFilter = new OwnerClickMessageFilter(ownerWindow, form.Close);

            void OnSettingsChanged(object? sender, SettingsFormChangedEventArgs e)
            {
                if (e.RefreshMainWindow)
                    _owner.ApplySettings(refreshCurrent: false, applyTheme: e.RefreshMainTheme);
                if (e.ReloadHotkeys)
                    _owner.ReloadHotkeys();
            }

            form.SettingsChanged += OnSettingsChanged;
            Application.AddMessageFilter(resizeFilter);
            Application.AddMessageFilter(ownerClickFilter);
            form.FormClosed += (sender, e) =>
            {
                Application.RemoveMessageFilter(resizeFilter);
                Application.RemoveMessageFilter(ownerClickFilter);
                ownerWindow.LocationChanged -= UpdateSettingsBounds;
                ownerWindow.ClientSizeChanged -= UpdateSettingsBounds;
                ownerWindow.SizeChanged -= UpdateSettingsBounds;
                form.SettingsChanged -= OnSettingsChanged;
                if (ReferenceEquals(_form, form))
                    _form = null;
            };

            form.Show(_owner.OwnerWindow);
        }

        private sealed class SettingsResizeMessageFilter(SettingsForm form) : IMessageFilter
        {
            private const int WmMouseMove = 0x0200;
            private const int WmLeftButtonDown = 0x0201;
            private const int WmNcLeftButtonDown = 0x00A1;

            public bool PreFilterMessage(ref Message m)
            {
                if (form.IsDisposed || !form.Visible || form.IsManualResizing)
                    return false;

                if (m.Msg is WmLeftButtonDown or WmNcLeftButtonDown)
                    return form.TryBeginManualResize(GetScreenPoint(m));

                if (m.Msg == WmMouseMove)
                {
                    if (form.TryGetResizeCursor(GetScreenPoint(m), out var resizeCursor))
                    {
                        form.Cursor = resizeCursor;
                        Cursor.Current = resizeCursor;
                        return true;
                    }

                    if (form.Cursor != Cursors.Default)
                        form.Cursor = Cursors.Default;
                }

                return false;
            }

            private static Point GetScreenPoint(Message message)
            {
                if (message.Msg == WmNcLeftButtonDown)
                    return new Point(unchecked((int)message.LParam.ToInt64()));

                long packed = message.LParam.ToInt64();
                var clientPoint = new Point(unchecked((short)(packed & 0xffff)),
                    unchecked((short)((packed >> 16) & 0xffff)));
                Control? target = Control.FromHandle(message.HWnd);
                return target == null ? Cursor.Position : target.PointToScreen(clientPoint);
            }
        }

        private sealed class OwnerClickMessageFilter(Form owner, Action closeSettings) : IMessageFilter
        {
            private const int WmLButtonDown = 0x0201;
            private const int WmRButtonDown = 0x0204;
            private const int WmMButtonDown = 0x0207;
            private const int WmNcLButtonDown = 0x00A1;
            private const int WmNcRButtonDown = 0x00A4;
            private const int WmNcMButtonDown = 0x00A7;

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg is not (WmLButtonDown or WmRButtonDown or WmMButtonDown or
                    WmNcLButtonDown or WmNcRButtonDown or WmNcMButtonDown))
                    return false;

                if (m.HWnd == owner.Handle)
                {
                    closeSettings();
                    return false;
                }

                Control? target = Control.FromHandle(m.HWnd);
                if (ReferenceEquals(target?.FindForm(), owner))
                    closeSettings();

                return false;
            }
        }
    }
}
