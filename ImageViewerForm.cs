using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace SpeedExplorer;

public class ImageViewerForm : Form
{
    // ... Imports for window dragging ...
    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    private readonly List<string> _imagePaths;
    private int _currentIndex;
    private Image? _currentImage;
    
    private readonly PictureBox _pictureBox;
    private readonly Panel _controlPanel;
    private readonly Panel _titleBar;
    private readonly Label _titleLabel;
    
    private readonly Panel _infoContainer;
    private readonly Label _fileNameLabel;
    private readonly Label _indexLabel;
    private readonly FlowLayoutPanel _tagsPanel;
    private readonly TrackBar _zoomSlider;
    private readonly Label _zoomLabel;
    
    private float _zoomLevel = 1.0f;
    private Point _panOffset = Point.Empty;
    private Point _lastMousePos;
    private bool _isPanning;
    private readonly AppSettings _settings = AppSettings.Current;
    private bool _isFullscreen;
    private FormWindowState _previousWindowState;
    private bool _autoFitEnabled = true;
    private bool _suppressZoomSliderEvent;
    
    private static readonly Color BackColor_Dark = Color.FromArgb(20, 20, 20);
    private static readonly Color ControlPanelColor = Color.FromArgb(40, 40, 40);
    private static readonly Color ForeColor_Dark = Color.FromArgb(240, 240, 240);
    private static readonly Color TitleBarColor = Color.FromArgb(32, 32, 32);

    private int Scale(int pixels) => (int)(pixels * (this.DeviceDpi / 96.0));
    private Padding Scale(Padding p) => new Padding(Scale(p.Left), Scale(p.Top), Scale(p.Right), Scale(p.Bottom));

    public ImageViewerForm(List<string> imagePaths, int startIndex)
    {
        _imagePaths = imagePaths;
        _currentIndex = Math.Clamp(startIndex, 0, imagePaths.Count - 1);

        // Form setup
        Text = "Speed Explorer"; // Generic title for taskbar
        var savedWidth = Math.Max(Scale(400), _settings.ImageViewerWidth);
        var savedHeight = Math.Max(Scale(300), _settings.ImageViewerHeight);
        Size = new Size(savedWidth, savedHeight);
        MinimumSize = new Size(Scale(400), Scale(300));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BackColor_Dark;
        KeyPreview = true; 
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None; 
        Padding = Scale(new Padding(2));

        // --- Title Bar ---
        _titleBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = Scale(30),
            BackColor = TitleBarColor,
            Padding = Scale(new Padding(10, 0, 0, 0))
        };
        // Manual double-click handling matching MainForm
        DateTime lastTitleBarClick = DateTime.MinValue;
        _titleBar.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                var now = DateTime.Now;
                if ((now - lastTitleBarClick).TotalMilliseconds < SystemInformation.DoubleClickTime)
                {
                    ToggleMaximize();
                    lastTitleBarClick = DateTime.MinValue; 
                }
                else
                {
                    lastTitleBarClick = now;
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, 0x2, 0);
                }
            }
        };

        _titleLabel = new Label
        {
            Text = "Image Viewer",
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(Scale(15), 0),
            Height = Scale(40)
        };
        // Manual double-click handling matching MainForm
        DateTime lastTitleLabelClick = DateTime.MinValue;
        _titleLabel.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                var now = DateTime.Now;
                if ((now - lastTitleLabelClick).TotalMilliseconds < SystemInformation.DoubleClickTime)
                {
                    ToggleMaximize();
                    lastTitleLabelClick = DateTime.MinValue; 
                }
                else
                {
                    lastTitleLabelClick = now;
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, 0x2, 0);
                }
            }
        };
        
        _titleBar.Controls.Add(_titleLabel);

        // Window Controls (Matching MainForm)
        var closeBtn = CreateWindowButton("X", "Close");
        closeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 17, 35);
        closeBtn.Click += (s, e) => Close();
        
        var maxBtn = CreateWindowButton("[ ]", "Maximize");
        maxBtn.Click += (s, e) => ToggleMaximize();

        var minBtn = CreateWindowButton("_", "Minimize");
        minBtn.Click += (s, e) => WindowState = FormWindowState.Minimized;
        
        // Manual positioning to match MainForm exactly
        _titleBar.Resize += (s, e) =>
        {
            closeBtn.Location = new Point(_titleBar.Width - Scale(46), 0);
            maxBtn.Location = new Point(_titleBar.Width - Scale(92), 0);
            minBtn.Location = new Point(_titleBar.Width - Scale(138), 0);
            _titleLabel.Width = _titleBar.Width - Scale(150); // Ensure it doesn't overlap buttons
        };
        
        // Add buttons
        _titleBar.Controls.Add(closeBtn);
        _titleBar.Controls.Add(maxBtn);
        _titleBar.Controls.Add(minBtn);

        // --- Picture Box ---
        _pictureBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor_Dark,
            SizeMode = PictureBoxSizeMode.Zoom 
        };
        _pictureBox.Paint += PictureBox_Paint;
        _pictureBox.MouseDown += PictureBox_MouseDown;
        _pictureBox.MouseMove += PictureBox_MouseMove;
        _pictureBox.MouseUp += PictureBox_MouseUp;
        _pictureBox.MouseWheel += PictureBox_MouseWheel;

        // --- Control Panel ---
        _controlPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = Scale(40),
            BackColor = ControlPanelColor,
            Padding = Scale(new Padding(10))
        };

        // Navigation
        var prevBtn = CreateButton("◀", Scale(50));
        prevBtn.Click += (s, e) => ShowPrevious();
        prevBtn.Location = new Point(Scale(10), Scale(15));

        var nextBtn = CreateButton("▶", Scale(50));
        nextBtn.Click += (s, e) => ShowNext();
        nextBtn.Location = new Point(Scale(70), Scale(15));

        // Info container to handle layout better
        _infoContainer = new Panel
        {
            Location = new Point(Scale(130), 0),
            Height = Scale(60),
            Width = Scale(400),
            BackColor = Color.Transparent
        };

        _fileNameLabel = new Label
        {
            AutoSize = true,
            Location = new Point(Scale(10), Scale(8)),
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _tagsPanel = new FlowLayoutPanel
        {
            Location = new Point(Scale(10), Scale(32)),
            Size = new Size(Scale(380), Scale(24)),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        _indexLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 9),
            TextAlign = ContentAlignment.MiddleLeft
        };
        // Position index label to the right of filename label dynamically
        _fileNameLabel.TextChanged += (s, e) => { _indexLabel.Location = new Point(_fileNameLabel.Right + Scale(10), _fileNameLabel.Top + Scale(2)); };

        _infoContainer.Controls.Add(_fileNameLabel);
        _infoContainer.Controls.Add(_indexLabel);
        _infoContainer.Controls.Add(_tagsPanel);

        // Zoom
        var zoomOutBtn = CreateButton("−", Scale(35));
        zoomOutBtn.Click += (s, e) => AdjustZoom(-0.1f);

        _zoomLabel = new Label { Text = "100%", AutoSize = false, Size = new Size(Scale(50), Scale(20)), ForeColor = ForeColor_Dark, Font = new Font("Segoe UI", 9), TextAlign = ContentAlignment.MiddleCenter };
        
        _zoomSlider = new TrackBar { Minimum = 10, Maximum = 500, Value = 100, Width = Scale(150), TickFrequency = 50, BackColor = ControlPanelColor };
        _zoomSlider.ValueChanged += (s, e) =>
        {
            if (_suppressZoomSliderEvent)
                return;
            _autoFitEnabled = false;
            _zoomLevel = _zoomSlider.Value / 100f;
            _zoomLabel.Text = $"{_zoomSlider.Value}%";
            _pictureBox.Invalidate();
        };

        var zoomInBtn = CreateButton("+", Scale(35));
        zoomInBtn.Click += (s, e) => AdjustZoom(0.1f);

        var fitBtn = CreateButton("Fit", Scale(50));
        fitBtn.Click += (s, e) => FitToWindow();

        var actualBtn = CreateButton("1:1", Scale(50));
        actualBtn.Click += (s, e) => ActualSize();

        var fullscreenBtn = CreateButton("⛶", Scale(40));
        fullscreenBtn.Click += (s, e) => ToggleFullscreen();

        // Add controls
        _controlPanel.Controls.AddRange(new Control[] { prevBtn, nextBtn, _infoContainer, zoomOutBtn, _zoomSlider, zoomInBtn, _zoomLabel, fitBtn, actualBtn, fullscreenBtn });
        _controlPanel.Resize += (s, e) => LayoutControls(); 

        Controls.Add(_pictureBox);
        Controls.Add(_controlPanel);
        Controls.Add(_titleBar); 

        // Custom Paint for Border
        Paint += (s, e) => { using var p = new Pen(Color.FromArgb(60, 60, 60)); e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); };

        LoadCurrentImage();
        LayoutControls();
        ApplySavedWindowState();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Handle specific keys and combinations
        if (keyData == Keys.Left || keyData == Keys.A)
        {
            ShowPrevious();
            return true;
        }
        if (keyData == Keys.Right || keyData == Keys.D || keyData == Keys.Space)
        {
            ShowNext();
            return true;
        }
        if (keyData == Keys.Escape)
        {
            if (_isFullscreen) ToggleFullscreen(); else Close();
            return true;
        }
        if (keyData == Keys.F || keyData == Keys.F11)
        {
            ToggleFullscreen();
            return true;
        }
        if (keyData == Keys.Add || keyData == Keys.Oemplus)
        {
            AdjustZoom(0.1f);
            return true;
        }
        if (keyData == Keys.Subtract || keyData == Keys.OemMinus)
        {
            AdjustZoom(-0.1f);
            return true;
        }
        
        // Handle Ctrl+0 and Ctrl+1
        // Note: Keys.Control is the modifier bit
        if (keyData == (Keys.Control | Keys.D0) || keyData == (Keys.Control | Keys.NumPad0))
        {
            FitToWindow();
            return true;
        }
        if (keyData == (Keys.Control | Keys.D1) || keyData == (Keys.Control | Keys.NumPad1))
        {
            ActualSize();
            return true;
        }
        // Also support plain 0 and 1 as fallback/alternate (as per user request implicit)
        if (keyData == Keys.D0 || keyData == Keys.NumPad0)
        {
             FitToWindow();
             return true;
        }
        if (keyData == Keys.D1 || keyData == Keys.NumPad1)
        {
             ActualSize();
             return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }



    private Button CreateWindowButton(string text, string tooltip)
    {
        var btn = new Button
        {
            Text = text,
            Size = new Size(Scale(46), Scale(40)),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 10),
            Cursor = Cursors.Hand,
            Margin = new Padding(0)
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);

        var tt = new ToolTip();
        tt.SetToolTip(btn, tooltip);

        return btn;
    }

    // ... ToggleMaximize, TitleBar_MouseDown (using new logic), etc ...
    // ToggleMaximize removed (duplicate)

    // Reuse existing layout logic
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        const int HTCLIENT = 1;
        const int HTLEFT = 10;
        const int HTRIGHT = 11;
        const int HTTOP = 12;
        const int HTTOPLEFT = 13;
        const int HTTOPRIGHT = 14;
        const int HTBOTTOM = 15;
        const int HTBOTTOMLEFT = 16;
        const int HTBOTTOMRIGHT = 17;

        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if (this.WindowState == FormWindowState.Normal && (int)m.Result == HTCLIENT)
            {
                Point screenPoint = new Point(m.LParam.ToInt32());
                Point clientPoint = this.PointToClient(screenPoint);
                int resizeBorder = Scale(15);
                if (clientPoint.Y <= resizeBorder)
                {
                    if (clientPoint.X <= resizeBorder) m.Result = (IntPtr)HTTOPLEFT;
                    else if (clientPoint.X >= (this.Size.Width - resizeBorder)) m.Result = (IntPtr)HTTOPRIGHT;
                    else m.Result = (IntPtr)HTTOP;
                }
                else if (clientPoint.Y >= (this.Size.Height - resizeBorder))
                {
                    if (clientPoint.X <= resizeBorder) m.Result = (IntPtr)HTBOTTOMLEFT;
                    else if (clientPoint.X >= (this.Size.Width - resizeBorder)) m.Result = (IntPtr)HTBOTTOMRIGHT;
                    else m.Result = (IntPtr)HTBOTTOM;
                }
                else
                {
                    if (clientPoint.X <= resizeBorder) m.Result = (IntPtr)HTLEFT;
                    else if (clientPoint.X >= (this.Size.Width - resizeBorder)) m.Result = (IntPtr)HTRIGHT;
                }
            }
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_autoFitEnabled && !_isFullscreen)
        {
            FitToWindow();
        }
    }

    private void LayoutControls()
    {
        if (_controlPanel == null || _controlPanel.Controls.Count < 10) return;
        var w = _controlPanel.ClientSize.Width;
        
        // Fullscreen btn
        _controlPanel.Controls[9].Location = new Point(w - Scale(50), Scale(15));
        // 1:1 btn
        _controlPanel.Controls[8].Location = new Point(w - Scale(105), Scale(15));
        // Fit btn
        _controlPanel.Controls[7].Location = new Point(w - Scale(160), Scale(15));
        
        _zoomLabel.Location = new Point(w - Scale(215), Scale(20));
        // Zoom in btn
        _controlPanel.Controls[5].Location = new Point(w - Scale(260), Scale(15));
        _zoomSlider.Location = new Point(w - Scale(420), Scale(12));
        // Zoom out btn
        _controlPanel.Controls[3].Location = new Point(w - Scale(460), Scale(15));

        // Info container width should adapt
        _infoContainer.Width = Math.Max(Scale(100), w - Scale(460) - Scale(130) - Scale(10));
        _tagsPanel.Width = _infoContainer.Width - Scale(20);
    }

    private Button CreateButton(string text, int width)
    {
        return new Button
        {
            Text = text,
            Size = new Size(width, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 10),
            Cursor = Cursors.Hand,
            FlatAppearance = { BorderSize = 0 }
        };
    }

    private void LoadCurrentImage()
    {
        if (_currentIndex < 0 || _currentIndex >= _imagePaths.Count) return;

        var path = _imagePaths[_currentIndex];
        
        try
        {
            _currentImage?.Dispose();
            _currentImage = ImageSharpViewerService.LoadBitmap(path);
            
            _fileNameLabel.Text = Path.GetFileName(path);
            _indexLabel.Text = $"{_currentIndex + 1} / {_imagePaths.Count}";
            _titleLabel.Text = $"Speed Explorer - {Path.GetFileName(path)}";

            UpdateTags(path);
            FitToWindow();
        }
        catch (SixLabors.ImageSharp.UnknownImageFormatException)
        {
            _currentImage = null;
            _fileNameLabel.Text = "Error: Format not supported";
        }
        catch (Exception ex)
        {
            _currentImage = null;
            _fileNameLabel.Text = $"Error: {ex.Message}";
        }
        _pictureBox.Invalidate();
    }

    private void UpdateTags(string path)
    {
        _tagsPanel.Controls.Clear();
        var tags = TagManager.Instance.GetTags(path);
        
        foreach (var tag in tags)
        {
            var tagLabel = new Label
            {
                Text = tag,
                AutoSize = true,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.LightGray,
                Font = new Font("Segoe UI", 8),
                Padding = Scale(new Padding(6, 2, 6, 2)),
                Margin = Scale(new Padding(0, 2, 4, 0))
            };
            
            // Standardizing pill look without full custom draw for now
            tagLabel.Paint += (s, e) =>
            {
                using var p = new Pen(Color.FromArgb(80, 80, 80));
                e.Graphics.DrawRectangle(p, 0, 0, tagLabel.Width - 1, tagLabel.Height - 1);
            };

            _tagsPanel.Controls.Add(tagLabel);
        }
    }

    private void PictureBox_Paint(object? sender, PaintEventArgs e)
    {
        if (_currentImage == null) return;

        e.Graphics.InterpolationMode = _zoomLevel > 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

        var imgWidth = (int)(_currentImage.Width * _zoomLevel);
        var imgHeight = (int)(_currentImage.Height * _zoomLevel);

        var x = (_pictureBox.Width - imgWidth) / 2 + _panOffset.X;
        var y = (_pictureBox.Height - imgHeight) / 2 + _panOffset.Y;

        e.Graphics.DrawImage(_currentImage, x, y, imgWidth, imgHeight);
    }

    private void PictureBox_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _isPanning = true;
            _lastMousePos = e.Location;
            _pictureBox.Cursor = Cursors.SizeAll;
        }
    }

    private void PictureBox_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            _panOffset.X += e.X - _lastMousePos.X;
            _panOffset.Y += e.Y - _lastMousePos.Y;
            _lastMousePos = e.Location;
            _pictureBox.Invalidate();
        }
    }

    private void PictureBox_MouseUp(object? sender, MouseEventArgs e)
    {
        _isPanning = false;
        _pictureBox.Cursor = Cursors.Default;
    }

    private void PictureBox_MouseWheel(object? sender, MouseEventArgs e)
    {
        if (_currentImage == null) return;

        float zoomDelta = e.Delta > 0 ? 0.1f : -0.1f;
        var oldZoom = _zoomLevel;
        var newZoom = Math.Clamp(_zoomLevel + zoomDelta, 0.1f, 5.0f);
        if (Math.Abs(newZoom - oldZoom) < 0.001f) return;

        var oldImgW = _currentImage.Width * oldZoom;
        var oldImgH = _currentImage.Height * oldZoom;
        var oldX = (_pictureBox.Width - oldImgW) / 2 + _panOffset.X;
        var oldY = (_pictureBox.Height - oldImgH) / 2 + _panOffset.Y;
        var mouseRelX = e.Location.X - oldX;
        var mouseRelY = e.Location.Y - oldY;

        _zoomLevel = newZoom;
        _zoomSlider.Value = (int)(_zoomLevel * 100);
        _zoomLabel.Text = $"{_zoomSlider.Value}%";

        var scaleFactor = newZoom / oldZoom;
        var newMouseRelX = mouseRelX * scaleFactor;
        var newMouseRelY = mouseRelY * scaleFactor;

        var expectedNewX = e.Location.X - newMouseRelX;
        var expectedNewY = e.Location.Y - newMouseRelY;

        var newImgW = _currentImage.Width * newZoom;
        var newImgH = _currentImage.Height * newZoom;

        _panOffset.X = (int)(expectedNewX - (_pictureBox.Width - newImgW) / 2);
        _panOffset.Y = (int)(expectedNewY - (_pictureBox.Height - newImgH) / 2);
        
        _pictureBox.Invalidate();
    }

    private void AdjustZoom(float delta)
    {
        _autoFitEnabled = false;
        _zoomLevel = Math.Clamp(_zoomLevel + delta, 0.1f, 5.0f);
        SetZoomSliderValue((int)(_zoomLevel * 100));
        _pictureBox.Invalidate();
    }

    private void FitToWindow()
    {
        if (_currentImage == null) return;
        var scaleX = (float)_pictureBox.Width / _currentImage.Width;
        var scaleY = (float)_pictureBox.Height / _currentImage.Height;
        if (_currentImage.Width <= _pictureBox.Width && _currentImage.Height <= _pictureBox.Height)
        {
            _zoomLevel = 1.0f;
        }
        else
        {
            _zoomLevel = Math.Min(scaleX, scaleY);
        }
        SetZoomSliderValue((int)(_zoomLevel * 100));
        _panOffset = Point.Empty;
        _pictureBox.Invalidate();
        _autoFitEnabled = true;
    }

    private void ActualSize()
    {
        _autoFitEnabled = false;
        _zoomLevel = 1.0f;
        SetZoomSliderValue(100);
        _panOffset = Point.Empty;
        _pictureBox.Invalidate();
    }

    private void ShowPrevious()
    {
        if (_currentIndex > 0)
        {
            _currentIndex--;
            LoadCurrentImage();
        }
    }

    private void ShowNext()
    {
        if (_currentIndex < _imagePaths.Count - 1)
        {
            _currentIndex++;
            LoadCurrentImage();
        }
    }

    private void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
        }
        else
        {
            // Respect taskbar
            MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
            WindowState = FormWindowState.Maximized;
        }
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            if (_previousWindowState == FormWindowState.Maximized)
            {
                // Force state reset to apply MaximizedBounds
                WindowState = FormWindowState.Normal;
                MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                WindowState = _previousWindowState;
            }
            
            _controlPanel.Visible = true;
            _titleBar.Visible = true;
            _isFullscreen = false;
        }
        else
        {
            _previousWindowState = WindowState;
            WindowState = FormWindowState.Normal; 
            FormBorderStyle = FormBorderStyle.None;
            // Fullscreen should cover taskbar, so clear bounds
            MaximizedBounds = Rectangle.Empty; 
            WindowState = FormWindowState.Maximized;
            _controlPanel.Visible = false;
            _titleBar.Visible = false;
            _isFullscreen = true;
        }
        FitToWindow();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveWindowState();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _currentImage?.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FitToWindow();
    }

    private void ApplySavedWindowState()
    {
        if (_settings.ImageViewerMaximized)
        {
            MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
            WindowState = FormWindowState.Maximized;
        }
    }

    private void SaveWindowState()
    {
        _settings.ImageViewerMaximized = WindowState == FormWindowState.Maximized;
        if (WindowState == FormWindowState.Normal)
        {
            _settings.ImageViewerWidth = Width;
            _settings.ImageViewerHeight = Height;
        }
        _settings.Save();
    }

    private void SetZoomSliderValue(int value)
    {
        _suppressZoomSliderEvent = true;
        _zoomSlider.Value = Math.Clamp(value, _zoomSlider.Minimum, _zoomSlider.Maximum);
        _zoomLabel.Text = $"{_zoomSlider.Value}%";
        _suppressZoomSliderEvent = false;
    }
}
