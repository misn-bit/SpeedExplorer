using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class WindowChromeController
    {
        private readonly MainForm _owner;
        private Point _titleBarMouseDownPos;
        private bool _isTitleBarMouseDown;
        private FormWindowState _preFullscreenState = FormWindowState.Normal;

        public WindowChromeController(MainForm owner)
        {
            _owner = owner;
        }

        public void RefreshFrame()
        {
            if (_owner.Handle == IntPtr.Zero) return;
            ApplyDarkMode();
            MARGINS margins = new() { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 0, cyBottomHeight = 0 };
            DwmExtendFrameIntoClientArea(_owner.Handle, ref margins);
            SetWindowPos(_owner.Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }

        public void ApplyDarkMode()
        {
            if (_owner.Handle == IntPtr.Zero) return;
            int darkMode = 1;
            DwmSetWindowAttribute(_owner.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            int borderColor = ColorTranslator.ToWin32(Color.FromArgb(45, 45, 48));
            DwmSetWindowAttribute(_owner.Handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
        }

        public void TitleBarMouseDown(Point location)
        {
            _isTitleBarMouseDown = true;
            _titleBarMouseDownPos = location;
        }

        public void TitleBarMouseUp()
        {
            _isTitleBarMouseDown = false;
        }

        public void TitleBarMouseMove(Control surface, MouseEventArgs e)
        {
            if (!_isTitleBarMouseDown || e.Button != MouseButtons.Left) return;

            int dx = e.X - _titleBarMouseDownPos.X;
            int dy = e.Y - _titleBarMouseDownPos.Y;
            if (Math.Abs(dx) <= 5 && Math.Abs(dy) <= 5) return;

            _isTitleBarMouseDown = false;
            if (_owner.WindowState == FormWindowState.Maximized)
            {
                var mousePos = surface.PointToClient(Cursor.Position);
                var oldWidth = _owner.Width;
                _owner.WindowState = FormWindowState.Normal;
                var newWidth = _owner.Width;
                var ratio = (double)mousePos.X / oldWidth;
                _owner.Left = Cursor.Position.X - (int)(newWidth * ratio);
                _owner.Top = Cursor.Position.Y - mousePos.Y;
            }

            ReleaseCapture();
            SendMessage(_owner.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }

        public Panel CreateTitleBar()
        {
            var titleBar = new Panel
            {
                Height = _owner.Scale(40),
                BackColor = MainForm.TitleBarColor
            };
            titleBar.Padding = new Padding(_owner.Scale(6), 0, _owner.Scale(6), 0);

            titleBar.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    TitleBarMouseDown(e.Location);
                    if (e.Clicks >= 2)
                    {
                        ToggleMaximize();
                        _isTitleBarMouseDown = false;
                    }
                }
            };
            titleBar.MouseUp += (s, e) => TitleBarMouseUp();
            titleBar.MouseMove += (s, e) => TitleBarMouseMove(titleBar, e);

            _owner._tabStrip = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                WrapContents = false,
                AutoScroll = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = MainForm.TitleBarColor,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };

            _owner._windowButtonsPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = _owner.Scale(138),
                BackColor = MainForm.TitleBarColor
            };

            _owner._windowCloseButton = CreateWindowButton("X", "Close");
            _owner._windowCloseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _owner._windowCloseButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 17, 35);
            _owner._windowCloseButton.Click += (s, e) => _owner.Close();

            _owner._windowMaxButton = CreateWindowButton("[ ]", "Maximize");
            _owner._windowMaxButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _owner._windowMaxButton.Click += (s, e) => ToggleMaximize();

            _owner._windowMinButton = CreateWindowButton("_", "Minimize");
            _owner._windowMinButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _owner._windowMinButton.Click += (s, e) => _owner.WindowState = FormWindowState.Minimized;

            void PositionWindowButtons()
            {
                if (_owner._windowButtonsPanel == null) return;
                _owner._windowCloseButton.Location = new Point(_owner._windowButtonsPanel.Width - _owner.Scale(46), 0);
                _owner._windowMaxButton.Location = new Point(_owner._windowButtonsPanel.Width - _owner.Scale(92), 0);
                _owner._windowMinButton.Location = new Point(_owner._windowButtonsPanel.Width - _owner.Scale(138), 0);
            }

            _owner._windowButtonsPanel.Resize += (s, e) => PositionWindowButtons();
            PositionWindowButtons();
            _owner._windowButtonsPanel.Controls.AddRange(new Control[] { _owner._windowMinButton, _owner._windowMaxButton, _owner._windowCloseButton });

            titleBar.Controls.Add(_owner._windowButtonsPanel);
            titleBar.Controls.Add(_owner._tabStrip);
            _owner._tabsController.AttachTabStrip(_owner._tabStrip, titleBar, _owner._windowButtonsPanel);

            return titleBar;
        }

        public Button CreateWindowButton(string text, string tooltip)
        {
            var btn = new Button
            {
                Text = text,
                Size = new Size(_owner.Scale(46), _owner.Scale(34)),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = MainForm.ForeColor_Dark,
                Font = new Font("Segoe UI", 10),
                Cursor = Cursors.Hand,
                Margin = _owner.Scale(new Padding(0))
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);

            var tt = new ToolTip();
            tt.SetToolTip(btn, tooltip);
            return btn;
        }

        public void ToggleMaximize()
        {
            if (_owner.WindowState == FormWindowState.Maximized)
            {
                _owner.WindowState = FormWindowState.Normal;
            }
            else
            {
                _owner.MaximizedBounds = Screen.FromControl(_owner).WorkingArea;
                _owner.WindowState = FormWindowState.Maximized;
            }
        }

        public void ToggleFullscreen()
        {
            bool isFullscreen = _owner.WindowState == FormWindowState.Maximized && _owner.MaximizedBounds == Rectangle.Empty;

            if (isFullscreen)
            {
                _owner.MaximizedBounds = Screen.FromControl(_owner).WorkingArea;
                if (_preFullscreenState == FormWindowState.Maximized)
                {
                    _owner.WindowState = FormWindowState.Normal;
                    _owner.WindowState = FormWindowState.Maximized;
                }
                else
                {
                    _owner.WindowState = FormWindowState.Normal;
                }
                return;
            }

            _preFullscreenState = _owner.WindowState;
            _owner.MaximizedBounds = Rectangle.Empty;
            if (_owner.WindowState == FormWindowState.Maximized)
            {
                _owner.WindowState = FormWindowState.Normal;
            }
            _owner.WindowState = FormWindowState.Maximized;
        }

        public bool HandleNcCalcSize(ref Message m)
        {
            if (m.Msg != WM_NCCALCSIZE) return false;
            m.Result = IntPtr.Zero;
            return true;
        }

        public void ApplyNcHitTest(ref Message m)
        {
            if (m.Msg != WM_NCHITTEST) return;
            if ((int)m.Result != 1) return; // HTCLIENT

            Point screenPoint = new(m.LParam.ToInt32());
            Point clientPoint = _owner.PointToClient(screenPoint);
            int b = _owner.Scale(8);
            int w = _owner.Width;
            int h = _owner.Height;

            if (clientPoint.Y <= b)
            {
                if (clientPoint.X <= b) m.Result = (IntPtr)HTTOPLEFT;
                else if (clientPoint.X >= w - b) m.Result = (IntPtr)HTTOPRIGHT;
                else m.Result = (IntPtr)HTTOP;
                return;
            }
            if (clientPoint.Y >= h - b)
            {
                if (clientPoint.X <= b) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (clientPoint.X >= w - b) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else m.Result = (IntPtr)HTBOTTOM;
                return;
            }
            if (clientPoint.X <= b) { m.Result = (IntPtr)HTLEFT; return; }
            if (clientPoint.X >= w - b) { m.Result = (IntPtr)HTRIGHT; return; }
        }

        public bool HandleNcPaintLike(ref Message m)
        {
            if (m.Msg != 0x0085 && m.Msg != 0x0086) return false;
            m.Result = (IntPtr)1;
            return true;
        }
    }
}
