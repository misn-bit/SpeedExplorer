using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
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
    private AnimatedImageSequence? _currentAnimation;
    private readonly System.Windows.Forms.Timer _animationTimer;
    private int _animationFrameIndex;
    
    private readonly PictureBox _pictureBox;
    private readonly Panel _contentPanel;
    private readonly Panel _controlPanel;
    private readonly Panel _titleBar;
    private readonly Label _titleLabel;
    
    private readonly Panel _infoContainer;
    private readonly Label _fileNameLabel;
    private readonly Label _indexLabel;
    private readonly FlowLayoutPanel _tagsPanel;
    private readonly TrackBar _zoomSlider;
    private readonly Label _zoomLabel;
    private readonly Panel _aiPanel;
    private readonly RichTextBox _aiOutputBox;
    private readonly Label _aiStatusLabel;
    private readonly TextBox _targetLanguageBox;
    private readonly CheckBox _overlayToggle;
    private readonly Button _prevBtn;
    private readonly Button _nextBtn;
    private readonly Button _zoomOutBtn;
    private readonly Button _zoomInBtn;
    private readonly Button _fitBtn;
    private readonly Button _actualBtn;
    private readonly Button _fullscreenBtn;
    private readonly Button _aiToggleBtn;
    private readonly Button _ocrBtn;
    private readonly Button _translateBtn;
    private readonly Button _tagBtn;
    private readonly Button _clearOverlayBtn;
    private readonly Button _copyResultBtn;
    private readonly LlmService _llmService = new();

    private float _zoomLevel = 1.0f;
    private Point _panOffset = Point.Empty;
    private Point _lastMousePos;
    private bool _isPanning;
    private readonly AppSettings _settings = AppSettings.Current;
    private bool _isFullscreen;
    private FormWindowState _previousWindowState;
    private bool _autoFitEnabled = true;
    private bool _suppressZoomSliderEvent;
    private bool _aiBusy;
    private string? _ocrImagePath;
    private LlmImageTextResult? _lastOcrResult;
    private List<string> _lastTranslations = new();
    private readonly List<OverlayTextBlock> _overlayBlocks = new();

    private sealed class OverlayTextBlock
    {
        public string SourceText { get; set; } = "";
        public string DisplayText { get; set; } = "";
        public RectangleF NormalizedRect { get; set; }
        public float NormalizedFontSize { get; set; }
    }
    
    private static readonly Color BackColor_Dark = Color.FromArgb(20, 20, 20);
    private static readonly Color ControlPanelColor = Color.FromArgb(40, 40, 40);
    private static readonly Color ForeColor_Dark = Color.FromArgb(240, 240, 240);
    private static readonly Color TitleBarColor = Color.FromArgb(32, 32, 32);

    private int Scale(int pixels) => (int)(pixels * (this.DeviceDpi / 96.0));
    private Padding Scale(Padding p) => new Padding(Scale(p.Left), Scale(p.Top), Scale(p.Right), Scale(p.Bottom));
    private int TitleBarHeight => Scale(32);
    private int ControlPanelHeight => Scale(50);
    private int ControlButtonHeight => Scale(24);
    private int ZoomSliderVisualOffsetY => Scale(2);

    public ImageViewerForm(List<string> imagePaths, int startIndex)
    {
        _imagePaths = imagePaths;
        _currentIndex = Math.Clamp(startIndex, 0, imagePaths.Count - 1);
        _animationTimer = new System.Windows.Forms.Timer();
        _animationTimer.Tick += AnimationTimer_Tick;

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
            Height = TitleBarHeight,
            BackColor = TitleBarColor,
            Padding = Scale(new Padding(8, 0, 0, 0))
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
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(Scale(12), 0),
            Height = TitleBarHeight
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
            closeBtn.Location = new Point(_titleBar.Width - closeBtn.Width, 0);
            maxBtn.Location = new Point(closeBtn.Left - maxBtn.Width, 0);
            minBtn.Location = new Point(maxBtn.Left - minBtn.Width, 0);
            _titleLabel.Width = Math.Max(Scale(80), minBtn.Left - Scale(12));
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

        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor_Dark
        };

        // --- AI Panel (Image Viewer) ---
        _aiPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = Scale(360),
            BackColor = Color.FromArgb(28, 28, 28),
            Padding = Scale(new Padding(8)),
            Visible = false
        };

        var aiActionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = Scale(30),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        _ocrBtn = CreateButton("OCR", Scale(58));
        _translateBtn = CreateButton("Translate", Scale(82));
        _tagBtn = CreateButton("Tag", Scale(58));
        aiActionRow.Controls.Add(_ocrBtn);
        aiActionRow.Controls.Add(_translateBtn);
        aiActionRow.Controls.Add(_tagBtn);

        var langRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = Scale(28),
            BackColor = Color.Transparent
        };
        var langLabel = new Label
        {
            AutoSize = true,
            Text = "Translate to:",
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Segoe UI", 8),
            Location = new Point(Scale(2), Scale(6))
        };
        _targetLanguageBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.Gainsboro,
            Font = new Font("Segoe UI", 8),
            Text = "English",
            Location = new Point(Scale(86), Scale(3)),
            Width = Scale(248)
        };
        langRow.Controls.Add(langLabel);
        langRow.Controls.Add(_targetLanguageBox);

        var aiToolsRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = Scale(28),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        _overlayToggle = new CheckBox
        {
            AutoSize = true,
            Text = "Show boxes",
            Checked = true,
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 8),
            BackColor = Color.Transparent,
            Margin = Scale(new Padding(0, 5, 8, 0))
        };
        _copyResultBtn = CreateButton("Copy", Scale(56));
        _clearOverlayBtn = CreateButton("Clear", Scale(56));
        aiToolsRow.Controls.Add(_overlayToggle);
        aiToolsRow.Controls.Add(_copyResultBtn);
        aiToolsRow.Controls.Add(_clearOverlayBtn);

        _aiStatusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = Scale(20),
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Segoe UI", 8),
            Text = "AI ready",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 0, 0, Scale(2))
        };

        _aiOutputBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            DetectUrls = false,
            WordWrap = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(35, 35, 35),
            ForeColor = Color.FromArgb(230, 230, 230),
            Font = new Font("Segoe UI", 9),
            HideSelection = false
        };

        _aiPanel.Controls.Add(_aiOutputBox);
        _aiPanel.Controls.Add(_aiStatusLabel);
        _aiPanel.Controls.Add(aiToolsRow);
        _aiPanel.Controls.Add(langRow);
        _aiPanel.Controls.Add(aiActionRow);

        _ocrBtn.Click += async (s, e) => await RunViewerOcrAsync(false);
        _translateBtn.Click += async (s, e) => await RunViewerOcrAsync(true);
        _tagBtn.Click += async (s, e) => await RunViewerTaggingAsync();
        _overlayToggle.CheckedChanged += (s, e) => _pictureBox.Invalidate();
        _clearOverlayBtn.Click += (s, e) =>
        {
            _overlayBlocks.Clear();
            _lastTranslations = new List<string>();
            _pictureBox.Invalidate();
            _aiStatusLabel.Text = "Overlay cleared";
        };
        _copyResultBtn.Click += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(_aiOutputBox.Text))
            {
                Clipboard.SetText(_aiOutputBox.Text);
                _aiStatusLabel.Text = "Copied to clipboard";
            }
        };

        _contentPanel.Controls.Add(_pictureBox);
        _contentPanel.Controls.Add(_aiPanel);

        // --- Control Panel ---
        _controlPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = ControlPanelHeight,
            BackColor = ControlPanelColor,
            Padding = Scale(new Padding(8, 6, 8, 6))
        };

        // Navigation
        _prevBtn = CreateButton("◀", Scale(50));
        _prevBtn.Click += (s, e) => ShowPrevious();
        _prevBtn.Location = new Point(Scale(8), Scale(15));

        _nextBtn = CreateButton("▶", Scale(50));
        _nextBtn.Click += (s, e) => ShowNext();
        _nextBtn.Location = new Point(Scale(64), Scale(15));

        // Info container to handle layout better
        _infoContainer = new Panel
        {
            Location = new Point(Scale(120), 0),
            Height = ControlPanelHeight,
            Width = Scale(400),
            BackColor = Color.Transparent
        };
        _infoContainer.Resize += (s, e) => LayoutInfoControls();

        _fileNameLabel = new Label
        {
            AutoSize = true,
            Location = new Point(Scale(8), Scale(4)),
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _tagsPanel = new FlowLayoutPanel
        {
            Location = new Point(Scale(8), Scale(24)),
            Size = new Size(Scale(380), Scale(18)),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        _indexLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _fileNameLabel.TextChanged += (s, e) => LayoutInfoControls();

        _infoContainer.Controls.Add(_fileNameLabel);
        _infoContainer.Controls.Add(_indexLabel);
        _infoContainer.Controls.Add(_tagsPanel);

        // Zoom
        _zoomOutBtn = CreateButton("−", Scale(35));
        _zoomOutBtn.Click += (s, e) => AdjustZoom(-0.1f);

        _zoomLabel = new Label { Text = "100%", AutoSize = false, Size = new Size(Scale(48), ControlButtonHeight), ForeColor = ForeColor_Dark, Font = new Font("Segoe UI", 8), TextAlign = ContentAlignment.MiddleCenter };
        
        _zoomSlider = new TrackBar { Minimum = 10, Maximum = 500, Value = 100, Width = Scale(150), TickStyle = TickStyle.None, AutoSize = true, TickFrequency = 50, BackColor = ControlPanelColor };
        _zoomSlider.ValueChanged += (s, e) =>
        {
            if (_suppressZoomSliderEvent)
                return;
            _autoFitEnabled = false;
            _zoomLevel = _zoomSlider.Value / 100f;
            _zoomLabel.Text = $"{_zoomSlider.Value}%";
            _pictureBox.Invalidate();
        };

        _zoomInBtn = CreateButton("+", Scale(35));
        _zoomInBtn.Click += (s, e) => AdjustZoom(0.1f);

        _fitBtn = CreateButton("Fit", Scale(50));
        _fitBtn.Click += (s, e) => FitToWindow();

        _actualBtn = CreateButton("1:1", Scale(50));
        _actualBtn.Click += (s, e) => ActualSize();

        _fullscreenBtn = CreateButton("⛶", Scale(40));
        _fullscreenBtn.Click += (s, e) => ToggleFullscreen();

        _aiToggleBtn = CreateButton("AI", Scale(42));
        _aiToggleBtn.Click += (s, e) => ToggleAiPanel();

        // Add controls
        _controlPanel.Controls.AddRange(new Control[] { _prevBtn, _nextBtn, _infoContainer, _zoomOutBtn, _zoomSlider, _zoomInBtn, _zoomLabel, _fitBtn, _actualBtn, _fullscreenBtn, _aiToggleBtn });
        _controlPanel.Resize += (s, e) => LayoutControls();

        Controls.Add(_contentPanel);
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
        // Do not trigger viewer hotkeys while target-language input is focused.
        if (_targetLanguageBox.Focused)
            return base.ProcessCmdKey(ref msg, keyData);

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
            Size = new Size(Scale(46), TitleBarHeight),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 9),
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
        if (_controlPanel == null)
            return;

        int w = _controlPanel.ClientSize.Width;
        int centerButtonY = Math.Max(Scale(1), (_controlPanel.ClientSize.Height - ControlButtonHeight) / 2);
        int sliderHeight = _zoomSlider.PreferredSize.Height;
        int sliderY = ((_controlPanel.ClientSize.Height - sliderHeight) / 2) + ZoomSliderVisualOffsetY;
        sliderY = Math.Clamp(sliderY, Scale(1), Math.Max(Scale(1), _controlPanel.ClientSize.Height - _zoomSlider.Height - Scale(1)));
        int spacing = Scale(6);

        int right = w - Scale(8);

        _aiToggleBtn.Location = new Point(right - _aiToggleBtn.Width, centerButtonY);
        right = _aiToggleBtn.Left - spacing;

        _fullscreenBtn.Location = new Point(right - _fullscreenBtn.Width, centerButtonY);
        right = _fullscreenBtn.Left - spacing;

        _actualBtn.Location = new Point(right - _actualBtn.Width, centerButtonY);
        right = _actualBtn.Left - spacing;

        _fitBtn.Location = new Point(right - _fitBtn.Width, centerButtonY);
        right = _fitBtn.Left - spacing;

        _zoomLabel.Location = new Point(right - _zoomLabel.Width, centerButtonY);
        right = _zoomLabel.Left - spacing;

        _zoomInBtn.Location = new Point(right - _zoomInBtn.Width, centerButtonY);
        right = _zoomInBtn.Left - spacing;

        _zoomSlider.Location = new Point(right - _zoomSlider.Width, sliderY);
        right = _zoomSlider.Left - spacing;

        _zoomOutBtn.Location = new Point(right - _zoomOutBtn.Width, centerButtonY);
        right = _zoomOutBtn.Left - spacing;

        _prevBtn.Location = new Point(Scale(8), centerButtonY);
        _nextBtn.Location = new Point(_prevBtn.Right + spacing, centerButtonY);

        int infoX = _nextBtn.Right + Scale(8);
        int infoWidth = Math.Max(Scale(100), right - infoX - Scale(8));
        _infoContainer.Location = new Point(infoX, 0);
        _infoContainer.Size = new Size(infoWidth, _controlPanel.ClientSize.Height);
        LayoutInfoControls();
    }

    private Button CreateButton(string text, int width)
    {
        return new Button
        {
            Text = text,
            Size = new Size(width, ControlButtonHeight),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 8),
            Cursor = Cursors.Hand,
            FlatAppearance = { BorderSize = 0 }
        };
    }

    private void LayoutInfoControls()
    {
        if (_infoContainer.Width <= 0 || _infoContainer.Height <= 0)
            return;

        int left = Scale(8);
        _fileNameLabel.Location = new Point(left, Scale(4));
        _indexLabel.Location = new Point(_fileNameLabel.Right + Scale(8), _fileNameLabel.Top + Scale(1));

        int tagsY = _fileNameLabel.Bottom + Scale(2);
        int tagsHeight = Math.Max(Scale(12), _infoContainer.Height - tagsY - Scale(4));
        _tagsPanel.Location = new Point(left, tagsY);
        _tagsPanel.Size = new Size(Math.Max(Scale(40), _infoContainer.Width - left * 2), tagsHeight);
    }

    private string? GetCurrentImagePath()
    {
        if (_currentIndex < 0 || _currentIndex >= _imagePaths.Count)
            return null;
        return _imagePaths[_currentIndex];
    }

    private void ToggleAiPanel()
    {
        _aiPanel.Visible = !_aiPanel.Visible;
        _aiToggleBtn.BackColor = _aiPanel.Visible ? Color.FromArgb(78, 78, 78) : Color.FromArgb(60, 60, 60);
        _aiToggleBtn.ForeColor = _aiPanel.Visible ? Color.White : ForeColor_Dark;
        _contentPanel.PerformLayout();
        LayoutControls();
        _pictureBox.Invalidate();
    }

    private void SetAiBusy(bool busy, string statusText)
    {
        _aiBusy = busy;
        _ocrBtn.Enabled = !busy;
        _translateBtn.Enabled = !busy;
        _tagBtn.Enabled = !busy;
        _targetLanguageBox.Enabled = !busy;
        _overlayToggle.Enabled = !busy;
        _clearOverlayBtn.Enabled = !busy;
        _copyResultBtn.Enabled = !busy;
        _aiStatusLabel.Text = statusText;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private async Task<string?> EnsureVisionModelAsync()
    {
        _llmService.ApiUrl = LlmService.GetCompletionsApiUrl(_settings.LlmApiUrl, null);
        return await _llmService.ResolveModelForTaskAsync(LlmUsageKind.Assistant, LlmTaskKind.Vision, this);
    }

    private async Task RunViewerOcrAsync(bool withTranslation)
    {
        if (_aiBusy)
            return;

        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        try
        {
            SetAiBusy(true, "Resolving model...");
            string? model = await EnsureVisionModelAsync();
            if (string.IsNullOrWhiteSpace(model))
            {
                SetAiBusy(false, "Model selection cancelled");
                return;
            }

            LlmImageTextResult? ocr = null;
            bool hasCachedOcr = withTranslation &&
                _lastOcrResult != null &&
                string.Equals(_ocrImagePath, imagePath, StringComparison.OrdinalIgnoreCase);

            if (hasCachedOcr)
            {
                ocr = _lastOcrResult;
                _aiStatusLabel.Text = "Using cached OCR...";
            }
            else
            {
                SetAiBusy(true, "Extracting text...");
                ocr = await _llmService.ExtractImageTextAsync(imagePath, model);
            }

            if (ocr == null)
            {
                SetAiBusy(false, "OCR failed");
                _aiOutputBox.Text = "Failed to extract text from the image.";
                return;
            }

            _ocrImagePath = imagePath;
            _lastOcrResult = ocr;
            if (!hasCachedOcr)
            {
                _lastTranslations = new List<string>();
                SetOverlayFromOcrResult(ocr, null);
                _aiOutputBox.Text = RenderOcrResult(ocr);
            }

            if (!withTranslation)
            {
                SetAiBusy(false, $"OCR complete ({ocr.Blocks.Count} blocks)");
                return;
            }

            SetAiBusy(true, "Translating...");
            string targetLanguage = string.IsNullOrWhiteSpace(_targetLanguageBox.Text) ? "English" : _targetLanguageBox.Text.Trim();
            var sourceBlocks = ocr.Blocks.Select(b => b.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (sourceBlocks.Count == 0 && !string.IsNullOrWhiteSpace(ocr.FullText))
                sourceBlocks.Add(ocr.FullText);

            var translation = await _llmService.TranslateTextBlocksAsync(sourceBlocks, targetLanguage, ocr.DetectedLanguage, model);
            if (translation == null)
            {
                SetAiBusy(false, "Translation failed");
                return;
            }

            _lastTranslations = translation.Translations;
            ApplyTranslationsToOverlay(_lastTranslations);
            _aiOutputBox.Text = RenderTranslatedResult(ocr, translation);
            SetAiBusy(false, $"Translated to {translation.TargetLanguage}");
        }
        catch (Exception ex)
        {
            SetAiBusy(false, $"AI error: {ex.Message}");
            LlmDebugLogger.LogError($"Image viewer OCR/translate failed: {ex}");
        }
    }

    private async Task RunViewerTaggingAsync()
    {
        if (_aiBusy)
            return;

        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        try
        {
            SetAiBusy(true, "Resolving model...");
            string? model = await EnsureVisionModelAsync();
            if (string.IsNullOrWhiteSpace(model))
            {
                SetAiBusy(false, "Model selection cancelled");
                return;
            }

            SetAiBusy(true, "Generating tags...");
            var tags = await _llmService.GetImageTagsAsync(
                "Analyze this image and return concise descriptive tags only. Prefer 8 to 20 tags.",
                imagePath,
                model);

            if (tags.Count == 0)
            {
                SetAiBusy(false, "No tags generated");
                _aiOutputBox.Text = "No tags were returned for this image.";
                return;
            }

            var normalized = tags
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            TagManager.Instance.UpdateTagsBatch(new[] { imagePath }, normalized, Enumerable.Empty<string>());
            UpdateTags(imagePath);

            _aiOutputBox.Text = "Applied tags:" + Environment.NewLine + string.Join(", ", normalized);
            SetAiBusy(false, $"Applied {normalized.Count} tags");
        }
        catch (Exception ex)
        {
            SetAiBusy(false, $"Tagging error: {ex.Message}");
            LlmDebugLogger.LogError($"Image viewer tagging failed: {ex}");
        }
    }

    private static RectangleF ClampNormalizedRect(float x, float y, float w, float h)
    {
        float nx = Math.Clamp(x, 0f, 1f);
        float ny = Math.Clamp(y, 0f, 1f);
        float nw = Math.Clamp(w, 0f, 1f);
        float nh = Math.Clamp(h, 0f, 1f);

        if (nx + nw > 1f)
            nw = 1f - nx;
        if (ny + nh > 1f)
            nh = 1f - ny;
        if (nw < 0f) nw = 0f;
        if (nh < 0f) nh = 0f;

        return new RectangleF(nx, ny, nw, nh);
    }

    private void SetOverlayFromOcrResult(LlmImageTextResult ocr, IReadOnlyList<string>? translatedLines)
    {
        _overlayBlocks.Clear();

        bool hasPixelCoordinates = ocr.Blocks.Any(b => b.X > 1.5f || b.Y > 1.5f || b.W > 1.5f || b.H > 1.5f);
        float minX = hasPixelCoordinates ? ocr.Blocks.Min(b => b.X) : 0f;
        float minY = hasPixelCoordinates ? ocr.Blocks.Min(b => b.Y) : 0f;
        float maxRight = hasPixelCoordinates ? ocr.Blocks.Max(b => b.X + b.W) : 1f;
        float maxBottom = hasPixelCoordinates ? ocr.Blocks.Max(b => b.Y + b.H) : 1f;

        float sourceW = _currentImage?.Width ?? 0f;
        float sourceH = _currentImage?.Height ?? 0f;

        float denomW = hasPixelCoordinates ? Math.Max(sourceW, maxRight) : 1f;
        float denomH = hasPixelCoordinates ? Math.Max(sourceH, maxBottom) : 1f;
        if (denomW <= 1f) denomW = Math.Max(1f, maxRight);
        if (denomH <= 1f) denomH = Math.Max(1f, maxBottom);

        // Some models return coordinates in a cropped/top-left canvas; stretch to extents when coverage is clearly compressed.
        float extentW = Math.Max(1f, maxRight - minX);
        float extentH = Math.Max(1f, maxBottom - minY);
        float coverW = sourceW > 1f ? (maxRight / sourceW) : 1f;
        float coverH = sourceH > 1f ? (maxBottom / sourceH) : 1f;
        bool stretchX = hasPixelCoordinates && sourceW > 1f && coverW < 0.90f;
        bool stretchY = hasPixelCoordinates && sourceH > 1f && coverH < 0.90f;

        for (int i = 0; i < ocr.Blocks.Count; i++)
        {
            var block = ocr.Blocks[i];
            float x;
            float y;
            float w;
            float h;

            if (hasPixelCoordinates)
            {
                x = stretchX ? ((block.X - minX) / extentW) : (block.X / denomW);
                y = stretchY ? ((block.Y - minY) / extentH) : (block.Y / denomH);
                w = stretchX ? (block.W / extentW) : (block.W / denomW);
                h = stretchY ? (block.H / extentH) : (block.H / denomH);
            }
            else
            {
                x = block.X;
                y = block.Y;
                w = block.W;
                h = block.H;
            }

            var rect = ClampNormalizedRect(x, y, w, h);
            if (rect.Width <= 0f || rect.Height <= 0f)
                continue;

            float normalizedFontSize = 0f;
            if (block.FontSize > 0f)
            {
                if (hasPixelCoordinates)
                {
                    float fontDenom = stretchY ? extentH : denomH;
                    if (fontDenom > 1f)
                        normalizedFontSize = block.FontSize / fontDenom;
                }
                else if (block.FontSize <= 1f)
                {
                    normalizedFontSize = block.FontSize;
                }
                else if (sourceH > 1f)
                {
                    normalizedFontSize = block.FontSize / sourceH;
                }

                normalizedFontSize = Math.Clamp(normalizedFontSize, 0f, 0.5f);
            }

            string translated = translatedLines != null && i < translatedLines.Count && !string.IsNullOrWhiteSpace(translatedLines[i])
                ? StripOrderedPrefix(translatedLines[i])
                : block.Text;

            _overlayBlocks.Add(new OverlayTextBlock
            {
                SourceText = block.Text,
                DisplayText = translated,
                NormalizedRect = rect,
                NormalizedFontSize = normalizedFontSize
            });
        }

        _pictureBox.Invalidate();
    }

    private void ApplyTranslationsToOverlay(IReadOnlyList<string> translatedLines)
    {
        if (_overlayBlocks.Count == 0)
            return;

        for (int i = 0; i < _overlayBlocks.Count; i++)
        {
            if (i < translatedLines.Count && !string.IsNullOrWhiteSpace(translatedLines[i]))
                _overlayBlocks[i].DisplayText = StripOrderedPrefix(translatedLines[i]);
        }

        _pictureBox.Invalidate();
    }

    private static string StripOrderedPrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        string trimmed = text.Trim();
        int i = 0;
        while (i < trimmed.Length && char.IsDigit(trimmed[i]))
            i++;

        if (i > 0 && i < trimmed.Length)
        {
            char marker = trimmed[i];
            if (marker == '.' || marker == ')' || marker == ':' || marker == '-')
            {
                i++;
                while (i < trimmed.Length && char.IsWhiteSpace(trimmed[i]))
                    i++;
                if (i < trimmed.Length)
                    return trimmed.Substring(i);
            }
        }

        return trimmed;
    }

    private static string RenderOcrResult(LlmImageTextResult ocr)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(ocr.DetectedLanguage))
            sb.AppendLine($"Detected language: {ocr.DetectedLanguage}");
        sb.AppendLine($"Blocks: {ocr.Blocks.Count}");
        sb.AppendLine();
        sb.AppendLine("Extracted text:");
        sb.AppendLine(string.IsNullOrWhiteSpace(ocr.FullText) ? "(no text)" : ocr.FullText.Trim());

        if (ocr.Blocks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Blocks:");
            for (int i = 0; i < ocr.Blocks.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {ocr.Blocks[i].Text}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderTranslatedResult(LlmImageTextResult ocr, LlmTextTranslationResult translation)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Detected language: {(string.IsNullOrWhiteSpace(ocr.DetectedLanguage) ? "unknown" : ocr.DetectedLanguage)}");
        sb.AppendLine($"Target language: {translation.TargetLanguage}");
        sb.AppendLine();
        sb.AppendLine("Translated text:");
        sb.AppendLine(string.IsNullOrWhiteSpace(translation.TranslatedFullText) ? "(empty)" : translation.TranslatedFullText.Trim());

        if (translation.Translations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Block mapping:");
            int count = Math.Max(ocr.Blocks.Count, translation.Translations.Count);
            for (int i = 0; i < count; i++)
            {
                string src = i < ocr.Blocks.Count ? ocr.Blocks[i].Text : "";
                string dst = i < translation.Translations.Count ? translation.Translations[i] : "";
                sb.AppendLine($"{i + 1}. {src}");
                sb.AppendLine($"   -> {dst}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private void LoadCurrentImage()
    {
        if (_currentIndex < 0 || _currentIndex >= _imagePaths.Count) return;

        var path = _imagePaths[_currentIndex];
        if (!string.Equals(_ocrImagePath, path, StringComparison.OrdinalIgnoreCase))
        {
            _ocrImagePath = null;
            _lastOcrResult = null;
            _lastTranslations = new List<string>();
            _overlayBlocks.Clear();
            _aiOutputBox.Clear();
            if (!_aiBusy)
                _aiStatusLabel.Text = "AI ready";
        }
        
        ClearAnimationState();
        try
        {
            _currentAnimation = ImageSharpViewerService.LoadAnimation(path);
            _animationFrameIndex = 0;
            _currentImage = _currentAnimation.GetFrame(_animationFrameIndex);
            StartAnimationIfNeeded();
            
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
                Font = new Font("Segoe UI", 7),
                Padding = Scale(new Padding(5, 1, 5, 1)),
                Margin = Scale(new Padding(0, 1, 4, 0))
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

        bool isUpscaling = _zoomLevel > 1.0f;
        e.Graphics.InterpolationMode = isUpscaling ? InterpolationMode.HighQualityBilinear : InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = isUpscaling ? PixelOffsetMode.None : PixelOffsetMode.HighQuality;
        e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
        e.Graphics.SmoothingMode = SmoothingMode.HighQuality;

        float imgWidth = _currentImage.Width * _zoomLevel;
        float imgHeight = _currentImage.Height * _zoomLevel;

        float x = (_pictureBox.Width - imgWidth) / 2f + _panOffset.X;
        float y = (_pictureBox.Height - imgHeight) / 2f + _panOffset.Y;

        var imageRect = new RectangleF(x, y, imgWidth, imgHeight);
        e.Graphics.DrawImage(_currentImage, imageRect);
        DrawOverlayBlocks(e.Graphics, imageRect);
    }

    private void DrawOverlayBlocks(Graphics g, RectangleF imageRect)
    {
        if (!_overlayToggle.Checked || _overlayBlocks.Count == 0)
            return;

        var priorHint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        try
        {
            using var fillBrush = new SolidBrush(Color.FromArgb(120, 7, 19, 36));
            using var borderPen = new Pen(Color.FromArgb(220, 125, 198, 255), 1.2f);
            using var badgeBrush = new SolidBrush(Color.FromArgb(220, 20, 20, 20));
            using var badgeBorder = new Pen(Color.FromArgb(220, 125, 198, 255), 1f);
            using var textBrush = new SolidBrush(Color.FromArgb(250, 250, 250));
            using var textBackBrush = new SolidBrush(Color.FromArgb(185, 0, 0, 0));
            float badgeFontPx = Math.Clamp(9f * _zoomLevel, 8f, 16f);
            using var badgeFont = new Font("Segoe UI", badgeFontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textFormat = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.Word,
                FormatFlags = StringFormatFlags.LineLimit
            };

            const float textInsetX = 4f;
            const float textInsetY = 3f;
            const float minTextFontPx = 10f;
            const float maxTextFontPx = 34f;
            const float modelFontScale = 1.25f;
            const float maxGrowWidthFactor = 1.60f;
            const float maxGrowHeightFactor = 2.00f;
            int maxShrinkSteps = _overlayBlocks.Count > 120 ? 12 : 40;
            int maxWidenSteps = _overlayBlocks.Count > 120 ? 3 : 8;
            int maxFinalShrinkSteps = _overlayBlocks.Count > 120 ? 8 : 24;
            var placedRects = new List<RectangleF>(_overlayBlocks.Count);

            for (int i = 0; i < _overlayBlocks.Count; i++)
            {
                var block = _overlayBlocks[i];
                float x = imageRect.X + (block.NormalizedRect.X * imageRect.Width);
                float y = imageRect.Y + (block.NormalizedRect.Y * imageRect.Height);
                float w = block.NormalizedRect.Width * imageRect.Width;
                float h = block.NormalizedRect.Height * imageRect.Height;

                if (w < 2f || h < 2f)
                    continue;

                var rect = new RectangleF(x, y, w, h);
                var drawRect = rect;
                string? text = string.IsNullOrWhiteSpace(block.DisplayText) ? null : block.DisplayText.Trim();
                RectangleF textRect = RectangleF.Empty;
                float textFontPx = minTextFontPx;

                if (!string.IsNullOrWhiteSpace(text))
                {
                    textRect = RectangleF.Inflate(rect, -textInsetX, -textInsetY);
                    if (textRect.Width >= 8f && textRect.Height >= 8f)
                    {
                        float modelFontPx = block.NormalizedFontSize > 0f ? block.NormalizedFontSize * imageRect.Height : 0f;
                        float autoFontPx = textRect.Height * 0.42f;
                        float baseFont = modelFontPx > 0f
                            ? Math.Clamp(modelFontPx * modelFontScale, minTextFontPx, maxTextFontPx)
                            : Math.Clamp(autoFontPx, minTextFontPx, maxTextFontPx);
                        textFontPx = Math.Min(baseFont, Math.Max(minTextFontPx, textRect.Height * 0.80f));

                        SizeF measured = MeasureTextForOverlay(g, text, textFontPx, textRect.Width, textFormat);

                        // If OCR gave a huge source box relative to text, shrink box to content first.
                        float compactTextW = Math.Clamp(measured.Width + 4f, 8f, textRect.Width);
                        float compactTextH = Math.Clamp(measured.Height + 4f, 8f, textRect.Height);
                        bool sourceBoxTooWide = textRect.Width > compactTextW * 1.35f;
                        bool sourceBoxTooTall = textRect.Height > compactTextH * 1.50f;
                        if (sourceBoxTooWide || sourceBoxTooTall)
                        {
                            textRect = new RectangleF(
                                textRect.X,
                                textRect.Y,
                                sourceBoxTooWide ? compactTextW : textRect.Width,
                                sourceBoxTooTall ? compactTextH : textRect.Height);
                            drawRect = RectangleF.Inflate(textRect, textInsetX, textInsetY);
                            measured = MeasureTextForOverlay(g, text, textFontPx, textRect.Width, textFormat);
                        }

                        // First shrink text toward min size.
                        int shrinkSteps = 0;
                        while (measured.Height > textRect.Height && textFontPx > minTextFontPx + 0.01f && shrinkSteps < maxShrinkSteps)
                        {
                            textFontPx = Math.Max(minTextFontPx, textFontPx - 0.75f);
                            measured = MeasureTextForOverlay(g, text, textFontPx, textRect.Width, textFormat);
                            shrinkSteps++;
                        }

                        // If still overflowing, widen text area to reduce wrapping.
                        float sourceTextWidth = Math.Max(8f, rect.Width - (textInsetX * 2f));
                        float maxTextWidth = Math.Min(
                            imageRect.Right - (textRect.X + 1f),
                            Math.Max(textRect.Width, sourceTextWidth * maxGrowWidthFactor));
                        int widenSteps = 0;
                        while (measured.Height > textRect.Height && textRect.Width < maxTextWidth - 0.5f && widenSteps < maxWidenSteps)
                        {
                            textRect.Width = Math.Min(maxTextWidth, textRect.Width * 1.20f);
                            measured = MeasureTextForOverlay(g, text, textFontPx, textRect.Width, textFormat);
                            widenSteps++;
                        }

                        // If text still does not fit, expand the box height.
                        if (measured.Height > textRect.Height)
                        {
                            float sourceTextHeight = Math.Max(8f, rect.Height - (textInsetY * 2f));
                            float maxTextHeight = Math.Min(
                                imageRect.Bottom - (textRect.Y + 1f),
                                Math.Max(textRect.Height, sourceTextHeight * maxGrowHeightFactor));
                            textRect.Height = Math.Min(maxTextHeight, measured.Height + 2f);
                        }

                        var desiredDrawRect = RectangleF.Union(drawRect, RectangleF.Inflate(textRect, textInsetX, textInsetY));
                        drawRect = ShiftRectIntoBounds(desiredDrawRect, imageRect);
                        textRect = RectangleF.Inflate(drawRect, -textInsetX, -textInsetY);

                        // One more safety pass after potential clamping/shift.
                        measured = MeasureTextForOverlay(g, text, textFontPx, Math.Max(1f, textRect.Width), textFormat);
                        int finalShrinkSteps = 0;
                        while (measured.Height > textRect.Height && textFontPx > minTextFontPx + 0.01f && finalShrinkSteps < maxFinalShrinkSteps)
                        {
                            textFontPx = Math.Max(minTextFontPx, textFontPx - 0.75f);
                            measured = MeasureTextForOverlay(g, text, textFontPx, Math.Max(1f, textRect.Width), textFormat);
                            finalShrinkSteps++;
                        }
                    }
                    else
                    {
                        text = null;
                    }
                }

                drawRect = ResolveOverlayCollision(drawRect, imageRect, placedRects);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    textRect = RectangleF.Inflate(drawRect, -textInsetX, -textInsetY);
                }

                g.FillRectangle(fillBrush, drawRect);
                g.DrawRectangle(borderPen, drawRect.X, drawRect.Y, drawRect.Width, drawRect.Height);

                string badgeText = (i + 1).ToString();
                var badgeSize = g.MeasureString(badgeText, badgeFont);
                var badgeRect = new RectangleF(
                    drawRect.X,
                    Math.Max(imageRect.Top, drawRect.Y - badgeSize.Height - 2f),
                    badgeSize.Width + 6f,
                    badgeSize.Height + 2f);

                g.FillRectangle(badgeBrush, badgeRect);
                g.DrawRectangle(badgeBorder, badgeRect.X, badgeRect.Y, badgeRect.Width, badgeRect.Height);
                g.DrawString(badgeText, badgeFont, textBrush, badgeRect.X + 3f, badgeRect.Y + 1f);

                if (!string.IsNullOrWhiteSpace(text) && textRect.Width > 4f && textRect.Height > 4f)
                {
                    g.FillRectangle(textBackBrush, textRect);
                    using var textFont = new Font("Segoe UI", textFontPx, FontStyle.Bold, GraphicsUnit.Pixel);
                    g.DrawString(text, textFont, textBrush, textRect, textFormat);
                }

                placedRects.Add(drawRect);
            }
        }
        finally
        {
            g.TextRenderingHint = priorHint;
        }
    }

    private static SizeF MeasureTextForOverlay(Graphics g, string text, float fontPx, float maxWidth, StringFormat format)
    {
        using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
        return g.MeasureString(text, font, new SizeF(Math.Max(1f, maxWidth), 10000f), format);
    }

    private static RectangleF ShiftRectIntoBounds(RectangleF rect, RectangleF bounds)
    {
        float width = Math.Min(rect.Width, bounds.Width);
        float height = Math.Min(rect.Height, bounds.Height);
        float x = rect.X;
        float y = rect.Y;

        if (x < bounds.X)
            x = bounds.X;
        if (y < bounds.Y)
            y = bounds.Y;

        if (x + width > bounds.Right)
            x = bounds.Right - width;
        if (y + height > bounds.Bottom)
            y = bounds.Bottom - height;

        return new RectangleF(x, y, width, height);
    }

    private static RectangleF ResolveOverlayCollision(RectangleF rect, RectangleF bounds, List<RectangleF> placedRects)
    {
        var baseRect = ShiftRectIntoBounds(rect, bounds);
        if (!HasHeavyOverlayOverlap(baseRect, placedRects))
            return baseRect;

        float step = Math.Clamp(Math.Min(baseRect.Width, baseRect.Height) * 0.12f, 4f, 14f);
        var directions = new (float dx, float dy)[]
        {
            (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f),
            (1f, 1f), (-1f, 1f), (1f, -1f), (-1f, -1f)
        };

        for (int ring = 1; ring <= 4; ring++)
        {
            foreach (var (dx, dy) in directions)
            {
                var shifted = new RectangleF(
                    baseRect.X + (dx * step * ring),
                    baseRect.Y + (dy * step * ring),
                    baseRect.Width,
                    baseRect.Height);
                shifted = ShiftRectIntoBounds(shifted, bounds);
                if (!HasHeavyOverlayOverlap(shifted, placedRects))
                    return shifted;
            }
        }

        return baseRect;
    }

    private static bool HasHeavyOverlayOverlap(RectangleF candidate, List<RectangleF> placedRects)
    {
        if (placedRects.Count == 0)
            return false;

        float candidateArea = Math.Max(1f, candidate.Width * candidate.Height);
        int start = Math.Max(0, placedRects.Count - 80);

        for (int i = start; i < placedRects.Count; i++)
        {
            var other = placedRects[i];
            float overlapW = Math.Min(candidate.Right, other.Right) - Math.Max(candidate.Left, other.Left);
            if (overlapW <= 0f)
                continue;

            float overlapH = Math.Min(candidate.Bottom, other.Bottom) - Math.Max(candidate.Top, other.Top);
            if (overlapH <= 0f)
                continue;

            float overlapArea = overlapW * overlapH;
            float otherArea = Math.Max(1f, other.Width * other.Height);
            float overlapRatio = overlapArea / Math.Min(candidateArea, otherArea);
            if (overlapRatio >= 0.42f)
                return true;
        }

        return false;
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
        ClearAnimationState();
        _animationTimer.Dispose();
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

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentAnimation == null || !_currentAnimation.IsAnimated)
        {
            _animationTimer.Stop();
            return;
        }

        _animationFrameIndex = (_animationFrameIndex + 1) % _currentAnimation.FrameCount;
        _currentImage = _currentAnimation.GetFrame(_animationFrameIndex);
        _animationTimer.Interval = _currentAnimation.GetFrameDelayMs(_animationFrameIndex);
        _pictureBox.Invalidate();
    }

    private void StartAnimationIfNeeded()
    {
        if (_currentAnimation == null || !_currentAnimation.IsAnimated)
        {
            _animationTimer.Stop();
            return;
        }

        _animationTimer.Interval = _currentAnimation.GetFrameDelayMs(_animationFrameIndex);
        _animationTimer.Start();
    }

    private void ClearAnimationState()
    {
        _animationTimer.Stop();
        _animationFrameIndex = 0;
        _currentImage = null;
        _currentAnimation?.Dispose();
        _currentAnimation = null;
    }
}
