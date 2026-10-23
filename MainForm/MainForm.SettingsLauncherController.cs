using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    Form ISettingsLauncherHost.OwnerWindow => this;
    bool ISettingsLauncherHost.IsSettingsPersistentChromePoint(Point screenPoint)
    {
        if (!_titleBar.IsHandleCreated || _titleBar.IsDisposed ||
            !_titleBar.RectangleToScreen(_titleBar.ClientRectangle).Contains(screenPoint))
            return false;

        // The tab controls are app actions, while unused tab-strip space remains a
        // title-bar drag surface. Keep that empty space interactive with Settings open.
        if (_tabStrip.IsHandleCreated && !_tabStrip.IsDisposed &&
            _tabStrip.RectangleToScreen(_tabStrip.ClientRectangle).Contains(screenPoint))
        {
            Point tabPoint = _tabStrip.PointToClient(screenPoint);
            return _tabStrip.GetChildAtPoint(tabPoint) == null;
        }

        return true;
    }

    Rectangle ISettingsLauncherHost.GetSettingsTabAreaBounds() =>
        _tabStrip.IsHandleCreated && !_tabStrip.IsDisposed
            ? _tabStrip.RectangleToScreen(_tabStrip.ClientRectangle)
            : Rectangle.Empty;
    Rectangle ISettingsLauncherHost.GetSettingsDimOverlayBounds(Rectangle ownerClientBounds)
    {
        if (!_titleBar.IsHandleCreated || _titleBar.IsDisposed)
            return ownerClientBounds;

        Rectangle titleBarBounds = _titleBar.RectangleToScreen(_titleBar.ClientRectangle);
        int top = Math.Clamp(titleBarBounds.Bottom, ownerClientBounds.Top, ownerClientBounds.Bottom);
        return Rectangle.FromLTRB(ownerClientBounds.Left, top, ownerClientBounds.Right, ownerClientBounds.Bottom);
    }
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
            var dimOverlay = new SettingsDimOverlayForm();
            var tabAreaOverlay = new SettingsDimOverlayForm();
            form.StartPosition = FormStartPosition.Manual;
            void UpdateSettingsBounds(object? sender = null, EventArgs? e = null)
            {
                if (!ownerWindow.IsDisposed && ownerWindow.IsHandleCreated && !form.IsDisposed)
                {
                    Rectangle clientBounds = ownerWindow.RectangleToScreen(ownerWindow.ClientRectangle);
                    if (!dimOverlay.IsDisposed)
                        dimOverlay.Bounds = _owner.GetSettingsDimOverlayBounds(clientBounds);
                    Rectangle tabAreaBounds = _owner.GetSettingsTabAreaBounds();
                    if (!tabAreaOverlay.IsDisposed)
                    {
                        if (tabAreaBounds.IsEmpty)
                        {
                            tabAreaOverlay.Hide();
                        }
                        else
                        {
                            tabAreaOverlay.Bounds = tabAreaBounds;
                            if (!tabAreaOverlay.Visible)
                                tabAreaOverlay.Show(ownerWindow);
                        }
                    }
                    form.SetOwnerClientBounds(clientBounds);
                }
            }

            UpdateSettingsBounds();
            ownerWindow.LocationChanged += UpdateSettingsBounds;
            ownerWindow.ClientSizeChanged += UpdateSettingsBounds;
            ownerWindow.SizeChanged += UpdateSettingsBounds;
            var resizeFilter = new SettingsResizeMessageFilter(form);
            var ownerClickFilter = new OwnerClickMessageFilter(
                ownerWindow, _owner.IsSettingsPersistentChromePoint, form.Close);

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
                if (!dimOverlay.IsDisposed)
                    dimOverlay.Close();
                if (!tabAreaOverlay.IsDisposed)
                    tabAreaOverlay.Close();
                Application.RemoveMessageFilter(resizeFilter);
                ownerClickFilter.RemoveAfterPendingClick();
                ownerWindow.LocationChanged -= UpdateSettingsBounds;
                ownerWindow.ClientSizeChanged -= UpdateSettingsBounds;
                ownerWindow.SizeChanged -= UpdateSettingsBounds;
                form.SettingsChanged -= OnSettingsChanged;
                if (ReferenceEquals(_form, form))
                    _form = null;
            };

            dimOverlay.Show(ownerWindow);
            form.Show(_owner.OwnerWindow);
        }

        private sealed class SettingsDimOverlayForm : Form
        {
            private const int WsExTransparent = 0x00000020;
            private const int WsExToolWindow = 0x00000080;
            private const int WsExNoActivate = 0x08000000;
            private const int WmNcHitTest = 0x0084;
            private const int HtTransparent = -1;

            public SettingsDimOverlayForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                ControlBox = false;
                BackColor = Color.Black;
                Opacity = 0.38;
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= WsExTransparent | WsExToolWindow | WsExNoActivate;
                    return cp;
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WmNcHitTest)
                {
                    m.Result = (IntPtr)HtTransparent;
                    return;
                }

                base.WndProc(ref m);
            }
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

        private sealed class OwnerClickMessageFilter(
            Form owner, Func<Point, bool> keepOpenOnPoint, Action closeSettings) : IMessageFilter
        {
            private const int WmLButtonDown = 0x0201;
            private const int WmLButtonUp = 0x0202;
            private const int WmRButtonDown = 0x0204;
            private const int WmRButtonUp = 0x0205;
            private const int WmMButtonDown = 0x0207;
            private const int WmMButtonUp = 0x0208;
            private const int WmNcLButtonDown = 0x00A1;
            private const int WmNcRButtonDown = 0x00A4;
            private const int WmNcMButtonDown = 0x00A7;
            private const int LeftButton = 1;
            private const int RightButton = 2;
            private const int MiddleButton = 4;
            private int _pendingButtonUps;
            private bool _removeRequested;

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool IsChild(IntPtr parentHandle, IntPtr childHandle);

            public void RemoveAfterPendingClick()
            {
                _removeRequested = true;
                if (_pendingButtonUps == 0)
                    Application.RemoveMessageFilter(this);
            }

            public bool PreFilterMessage(ref Message m)
            {
                int releasedButton = m.Msg switch
                {
                    WmLButtonUp => LeftButton,
                    WmRButtonUp => RightButton,
                    WmMButtonUp => MiddleButton,
                    _ => 0
                };

                if (releasedButton != 0 && (_pendingButtonUps & releasedButton) != 0)
                {
                    _pendingButtonUps &= ~releasedButton;
                    if (_removeRequested && _pendingButtonUps == 0)
                        Application.RemoveMessageFilter(this);
                    return true;
                }

                if (m.Msg is not (WmLButtonDown or WmRButtonDown or WmMButtonDown or
                    WmNcLButtonDown or WmNcRButtonDown or WmNcMButtonDown))
                    return false;

                bool nonClient = m.Msg is WmNcLButtonDown or WmNcRButtonDown or WmNcMButtonDown;
                Point screenPoint;
                if (nonClient)
                {
                    long packed = m.LParam.ToInt64();
                    screenPoint = new Point(unchecked((short)(packed & 0xffff)),
                        unchecked((short)((packed >> 16) & 0xffff)));
                }
                else
                {
                    long packed = m.LParam.ToInt64();
                    var clientPoint = new Point(unchecked((short)(packed & 0xffff)),
                        unchecked((short)((packed >> 16) & 0xffff)));
                    Control? messageTarget = Control.FromHandle(m.HWnd);
                    screenPoint = messageTarget == null
                        ? Cursor.Position
                        : messageTarget.PointToScreen(clientPoint);
                }

                // The title bar controls window management, dragging and tabs. Non-client
                // input covers resizing the owner. Neither should dismiss the settings overlay.
                if (nonClient || keepOpenOnPoint(screenPoint))
                    return false;

                Control? target = Control.FromHandle(m.HWnd);
                bool targetsOwner = m.HWnd == owner.Handle ||
                    IsChild(owner.Handle, m.HWnd) ||
                    ReferenceEquals(target?.FindForm(), owner);
                if (targetsOwner)
                {
                    _pendingButtonUps |= m.Msg switch
                    {
                        WmLButtonDown => LeftButton,
                        WmRButtonDown => RightButton,
                        WmMButtonDown => MiddleButton,
                        _ => 0
                    };
                    closeSettings();
                    // Dismiss Settings without letting this same click activate the
                    // underlying control (for example, sort, open a file, or act on a tab).
                    return true;
                }

                return false;
            }
        }
    }
}
