using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Collections.Specialized;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace SpeedExplorer;

public sealed class ImageViewerSortOptions
{
    public ImageViewerSortOptions(SortColumn column, SortDirection direction, bool taggedFilesOnTop)
    {
        Column = column;
        Direction = direction;
        TaggedFilesOnTop = taggedFilesOnTop;
    }

    public SortColumn Column { get; }
    public SortDirection Direction { get; }
    public bool TaggedFilesOnTop { get; }
}

public class ImageViewerForm : Form
{
    // ... Imports for window dragging ...
    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);
    private const uint SEE_MASK_INVOKEIDLIST = 0xC;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb;
        public string lpFile;
        public string lpParameters;
        public string lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIconOrMonitor;
        public IntPtr hProcess;
    }

    private readonly List<string> _imagePaths;
    private int _currentIndex;
    private Image? _currentImage;
    private AnimatedImageSequence? _currentAnimation;
    private readonly System.Windows.Forms.Timer _animationTimer;
    private int _animationFrameIndex;
    
    private readonly PictureBox _pictureBox;
    private readonly ContextMenuStrip _imageContextMenu;
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
    private readonly TextBox _sourceLanguageHintBox;
    private readonly TextBox _ocrHintBox;
    private readonly TextBox _translationContextHintBox;
    private readonly CheckBox _manualMaxEffortCheck;
    private readonly CheckBox _ocrReasoningCheck;
    private readonly CheckBox _translationReasoningCheck;
    private readonly CheckBox _overlayToggle;
    private readonly CheckBox _showSavedOcrCheck;
    private readonly CheckBox _showSavedTranslationCheck;
    private readonly Button _prevBtn;
    private readonly Button _nextBtn;
    private readonly Button _zoomOutBtn;
    private readonly Button _zoomInBtn;
    private readonly Button _fitBtn;
    private readonly Button _actualBtn;
    private readonly Button _rotateBtn;
    private readonly Button _fullscreenBtn;
    private readonly Button _aiToggleBtn;
    private readonly Button _ocrBtn;
    private readonly Button _translateBtn;
    private readonly Button _drawOcrBoxBtn;
    private readonly Button _clearManualOcrBoxesBtn;
    private readonly Button _tagBtn;
    private readonly Button _clearOverlayBtn;
    private readonly Button _deleteSavedTranslationBtn;
    private readonly Button _copyResultBtn;
    private readonly Button _abortBtn;
    private readonly Button _cancelCurrentJobBtn;
    private readonly Button _openSavedOcrFileBtn;
    private readonly Button _closeBtn;
    private ToolStripMenuItem _editOverlayBlockMenuItem = null!;
    private readonly LlmService _llmService = new();
    private CancellationTokenSource? _aiCts;
    private CancellationTokenSource? _tagCts;
    private string? _activeTagImagePath;
    private FileSystemWatcher? _imageFolderWatcher;
    private readonly System.Windows.Forms.Timer _imageFolderRefreshTimer;
    private string? _watchedImageFolder;

    private float _zoomLevel = 1.0f;
    private Point _panOffset = Point.Empty;
    private Point _lastMousePos;
    private bool _isPanning;
    private DateTime _lastPictureBoxLeftMouseUpUtc = DateTime.MinValue;
    private DateTime _pictureBoxSecondClickDownUtc = DateTime.MinValue;
    private Point _lastPictureBoxLeftMouseUpPoint;
    private readonly AppSettings _settings = AppSettings.Current;
    private bool _isFullscreen;
    private FormWindowState _previousWindowState;
    private bool _autoFitEnabled = true;
    private bool _autoFitBySmallerDimension;
    private int _rotationQuarterTurns;
    private bool _suppressZoomSliderEvent;
    private bool _aiBusy;
    private string? _ocrImagePath;
    private LlmImageTextResult? _lastOcrResult;
    private LlmTextTranslationResult? _savedTranslationForCurrentImage;
    private List<string> _lastTranslations = new();
    private readonly List<OverlayTextBlock> _overlayBlocks = new();
    private OverlayStyleDefaults? _currentImageOverlayDefaults;
    private bool _currentOverlayFromSavedCache;
    private bool _suppressSavedTranslationToggleEvent;
    private bool _showSavedTranslationPreferred;
    private bool _manualOcrDrawMode;
    private bool _isDrawingManualOcrRegion;
    private bool _cornerCloseHover;
    private int _contextOverlayBlockIndex = -1;
    private OverlayDragMode _overlayDragMode = OverlayDragMode.None;
    private int _overlayDragBlockIndex = -1;
    private string? _overlayDragImagePath;
    private Point _overlayDragStartPoint;
    private RectangleF _overlayDragStartRect;
    private bool _overlayDragStartHadUserOverride;
    private bool _overlayDragChanged;
    private Point _manualOcrDragStart;
    private Point _manualOcrDragCurrent;
    private readonly List<ManualOcrRegion> _pendingManualOcrRegions = new();
    private readonly List<ImageAiJob> _queuedAiJobs = new();
    private readonly Dictionary<string, int> _queuedAiJobCountsByImage = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<RectangleF>> _queuedManualRegionsByImage = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<RectangleF>> _restorableManualRegionsByImage = new(StringComparer.OrdinalIgnoreCase);
    private ImageAiJob? _activeAiJob;
    private bool _cancelActiveAiJobOnly;

    private sealed class OverlayTextBlock
    {
        public int SourceIndex { get; set; }
        public string SourceText { get; set; } = "";
        public string DisplayText { get; set; } = "";
        public RectangleF NormalizedRect { get; set; }
        public float NormalizedFontSize { get; set; }
        public int? TextColorArgb { get; set; }
        public int? TextOutlineColorArgb { get; set; }
        public StringAlignment? TextAlignment { get; set; }
        public StringAlignment? TextVerticalAlignment { get; set; }
        public bool? TextOutlineVisible { get; set; }
        public int? BoxFillColorArgb { get; set; }
        public int? BoxBorderColorArgb { get; set; }
        public bool? BoxFillVisible { get; set; }
        public bool? BoxBorderVisible { get; set; }
        public bool IsManualBox { get; set; }
        public bool IsPendingManualBox { get; set; }
        public bool HasUserOverride { get; set; }
    }

    private enum OverlayDragMode
    {
        None,
        Move,
        ResizeLeft,
        ResizeRight,
        ResizeTop,
        ResizeBottom,
        ResizeTopLeft,
        ResizeTopRight,
        ResizeBottomLeft,
        ResizeBottomRight
    }

    private sealed class ManualOcrRegion
    {
        public RectangleF NormalizedRect { get; set; }
    }

    private sealed class ManualOcrSnippet
    {
        public RectangleF NormalizedRect { get; set; }
        public string TempPath { get; set; } = "";
    }

    private sealed class ImageAiJob
    {
        public string ImagePath { get; set; } = "";
        public bool WithTranslation { get; set; }
        public bool UseMaximumEffortManualTranslation { get; set; }
        public bool UseOcrReasoning { get; set; }
        public bool UseTranslationReasoning { get; set; }
        public string TargetLanguage { get; set; } = "English";
        public string SourceLanguageHint { get; set; } = "";
        public string OcrHint { get; set; } = "";
        public string TranslationContextHint { get; set; } = "";
        public string? ModelId { get; set; }
        public List<ManualOcrSnippet> ManualSnippets { get; set; } = new();
    }

    private sealed class ImageAiJobResult
    {
        public string ImagePath { get; set; } = "";
        public string StatusText { get; set; } = "";
        public string? ErrorText { get; set; }
        public bool FromSavedCache { get; set; }
        public bool ShowSavedTranslation { get; set; }
        public LlmImageTextResult? Ocr { get; set; }
        public LlmTextTranslationResult? Translation { get; set; }
    }

    private sealed class OcrCacheEnvelope
    {
        public string SourcePath { get; set; } = "";
        public long SourceLength { get; set; }
        public long SourceLastWriteUtcTicks { get; set; }
        public long SavedUtcTicks { get; set; }
        public string ModelId { get; set; } = "";
        public LlmImageTextResult? Result { get; set; }
        public string TranslationTargetLanguage { get; set; } = "";
        public string TranslationSourceLanguage { get; set; } = "";
        public string TranslationModelId { get; set; } = "";
        public string TranslationFullText { get; set; } = "";
        public List<string> TranslationLines { get; set; } = new();
        public long TranslationSavedUtcTicks { get; set; }
        public OverlayStyleDefaults? OverlayDefaults { get; set; }
        public List<OcrOverlayBlockOverride> OverlayOverrides { get; set; } = new();
    }

    private sealed class OcrOverlayBlockOverride
    {
        public int SourceIndex { get; set; }
        public string? Text { get; set; }
        public string? TranslationText { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float W { get; set; }
        public float H { get; set; }
        public float FontSize { get; set; }
        public int? TextColorArgb { get; set; }
        public int? TextOutlineColorArgb { get; set; }
        public StringAlignment? TextAlignment { get; set; }
        public StringAlignment? TextVerticalAlignment { get; set; }
        public bool? TextOutlineVisible { get; set; }
        public int? BoxFillColorArgb { get; set; }
        public int? BoxBorderColorArgb { get; set; }
        public bool? BoxFillVisible { get; set; }
        public bool? BoxBorderVisible { get; set; }
    }

    private sealed class OverlayBlockEditResult
    {
        public string OcrText { get; set; } = "";
        // Null means the caller is changing only geometry/text and must preserve the
        // existing translation. An empty string is an intentional clear from the editor.
        public string? TranslationText { get; set; }
        public RectangleF NormalizedRect { get; set; }
        public float NormalizedFontSize { get; set; }
        public int? TextColorArgb { get; set; }
        public int? TextOutlineColorArgb { get; set; }
        public StringAlignment? TextAlignment { get; set; }
        public StringAlignment? TextVerticalAlignment { get; set; }
        public bool? TextOutlineVisible { get; set; }
        public int? BoxFillColorArgb { get; set; }
        public int? BoxBorderColorArgb { get; set; }
        public bool? BoxFillVisible { get; set; }
        public bool? BoxBorderVisible { get; set; }
        public bool StyleSettingsChanged { get; set; }
    }

    private const string OcrCacheJsonSeparator = "###__OCR_CACHE_JSON__###";
    
    private static readonly Color BackColor_Dark = Color.FromArgb(20, 20, 20);
    private static readonly Color ControlPanelColor = Color.FromArgb(40, 40, 40);
    private static readonly Color ForeColor_Dark = Color.FromArgb(240, 240, 240);
    private static readonly Color TitleBarColor = Color.FromArgb(32, 32, 32);
    private static readonly Color DefaultOverlayFillColor = Color.FromArgb(242, 7, 19, 36);
    private static readonly Color DefaultOverlayBorderColor = Color.FromArgb(220, 125, 198, 255);
    private static readonly Color DefaultOverlayTextColor = Color.FromArgb(250, 250, 250);
    private static readonly Color DefaultOverlayTextOutlineColor = Color.FromArgb(255, 0, 0, 0);

    private int Scale(int pixels) => (int)(pixels * (this.DeviceDpi / 96.0));
    private Padding Scale(Padding p) => new Padding(Scale(p.Left), Scale(p.Top), Scale(p.Right), Scale(p.Bottom));
    private int TitleBarHeight => Scale(32);
    private int ControlPanelHeight => Scale(50);
    private int ControlButtonHeight => Scale(24);
    private int ZoomSliderVisualOffsetY => Scale(2);
    private Padding WindowFramePadding => Scale(new Padding(2));

    private readonly ImageViewerSortOptions? _sortOptions;

    public ImageViewerForm(List<string> imagePaths, int startIndex, ImageViewerSortOptions? sortOptions = null)
    {
        _imagePaths = imagePaths;
        _sortOptions = sortOptions;
        _currentIndex = Math.Clamp(startIndex, 0, imagePaths.Count - 1);
        _animationTimer = new System.Windows.Forms.Timer();
        _animationTimer.Tick += AnimationTimer_Tick;
        _imageFolderRefreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _imageFolderRefreshTimer.Tick += ImageFolderRefreshTimer_Tick;

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
        Padding = WindowFramePadding;

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
        _closeBtn = CreateWindowButton("X", "Close");
        _closeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 17, 35);
        _closeBtn.Click += (s, e) => Close();
        
        var maxBtn = CreateWindowButton("[ ]", "Maximize");
        maxBtn.Click += (s, e) => ToggleMaximize();

        var minBtn = CreateWindowButton("_", "Minimize");
        minBtn.Click += (s, e) => WindowState = FormWindowState.Minimized;
        
        // Manual positioning to match MainForm exactly
        _titleBar.Resize += (s, e) =>
        {
            _closeBtn.Location = new Point(_titleBar.Width - _closeBtn.Width, 0);
            maxBtn.Location = new Point(_closeBtn.Left - maxBtn.Width, 0);
            minBtn.Location = new Point(maxBtn.Left - minBtn.Width, 0);
            _titleLabel.Width = Math.Max(Scale(80), minBtn.Left - Scale(12));
        };
        
        // Add buttons
        _titleBar.Controls.Add(_closeBtn);
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
        _pictureBox.MouseDoubleClick += PictureBox_MouseDoubleClick;
        _pictureBox.MouseWheel += PictureBox_MouseWheel;
        _imageContextMenu = BuildImageContextMenu();
        _pictureBox.ContextMenuStrip = _imageContextMenu;

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
        _drawOcrBoxBtn = CreateButton("Draw OCR Box", Scale(102));
        _clearManualOcrBoxesBtn = CreateButton("Clear Boxes", Scale(82));
        _tagBtn = CreateButton("Tag", Scale(58));
        aiActionRow.Controls.Add(_ocrBtn);
        aiActionRow.Controls.Add(_translateBtn);
        aiActionRow.Controls.Add(_drawOcrBoxBtn);
        aiActionRow.Controls.Add(_clearManualOcrBoxesBtn);
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
            Text = string.IsNullOrWhiteSpace(_settings.ImageViewerTargetLanguage)
                ? "English"
                : _settings.ImageViewerTargetLanguage,
            Location = new Point(Scale(86), Scale(3)),
            Width = Scale(248)
        };
        langRow.Controls.Add(langLabel);
        langRow.Controls.Add(_targetLanguageBox);

        var sourceHintRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = Scale(28),
            BackColor = Color.Transparent
        };
        var sourceHintLabel = new Label
        {
            AutoSize = true,
            Text = "Source hint:",
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Segoe UI", 8),
            Location = new Point(Scale(2), Scale(6))
        };
        _sourceLanguageHintBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.Gainsboro,
            Font = new Font("Segoe UI", 8),
            Text = _settings.ImageViewerSourceLanguageHint ?? "",
            Location = new Point(Scale(86), Scale(3)),
            Width = Scale(248)
        };
        sourceHintRow.Controls.Add(sourceHintLabel);
        sourceHintRow.Controls.Add(_sourceLanguageHintBox);

        var ocrHintRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = Scale(46),
            BackColor = Color.Transparent
        };
        var ocrHintLabel = new Label
        {
            AutoSize = true,
            Text = "OCR hint:",
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Segoe UI", 8),
            Location = new Point(Scale(2), Scale(6))
        };
        _ocrHintBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.Gainsboro,
            Font = new Font("Segoe UI", 8),
            Text = _settings.ImageViewerOcrHint ?? "",
            Location = new Point(Scale(86), Scale(3)),
            Width = Scale(248),
            Height = Scale(38),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical
        };
        ocrHintRow.Controls.Add(ocrHintLabel);
        ocrHintRow.Controls.Add(_ocrHintBox);

        var contextHintRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = Scale(46),
            BackColor = Color.Transparent
        };
        var contextHintLabel = new Label
        {
            AutoSize = true,
            Text = "Context:",
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Segoe UI", 8),
            Location = new Point(Scale(2), Scale(6))
        };
        _translationContextHintBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.Gainsboro,
            Font = new Font("Segoe UI", 8),
            Text = _settings.ImageViewerTranslationContextHint ?? "",
            Location = new Point(Scale(86), Scale(3)),
            Width = Scale(248),
            Height = Scale(38),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical
        };
        contextHintRow.Controls.Add(contextHintLabel);
        contextHintRow.Controls.Add(_translationContextHintBox);

        var manualModeRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = Scale(24),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _manualMaxEffortCheck = new CheckBox
        {
            AutoSize = true,
            Text = "Max effort translate",
            Checked = _settings.ImageViewerManualMaxEffortTranslation,
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 8),
            BackColor = Color.Transparent,
            Margin = new Padding(0, Scale(3), 0, 0)
        };
        manualModeRow.Controls.Add(_manualMaxEffortCheck);

        var reasoningRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = Scale(24),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _ocrReasoningCheck = new CheckBox
        {
            AutoSize = true,
            Text = "OCR reasoning",
            Checked = _settings.ImageViewerOcrReasoningEnabled,
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 8),
            BackColor = Color.Transparent,
            Margin = new Padding(0, Scale(3), Scale(12), 0)
        };
        _translationReasoningCheck = new CheckBox
        {
            AutoSize = true,
            Text = "Translation reasoning",
            Checked = _settings.ImageViewerTranslationReasoningEnabled,
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 8),
            BackColor = Color.Transparent,
            Margin = new Padding(0, Scale(3), 0, 0)
        };
        reasoningRow.Controls.Add(_ocrReasoningCheck);
        reasoningRow.Controls.Add(_translationReasoningCheck);

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
            Checked = _settings.ImageViewerOverlayBoxesVisible,
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 8),
            BackColor = Color.Transparent,
            Margin = Scale(new Padding(0, 5, 8, 0))
        };
        _copyResultBtn = CreateButton("Copy", Scale(56));
        _abortBtn = CreateButton("Abort", Scale(56));
        _abortBtn.ForeColor = Color.Salmon;
        _abortBtn.Visible = false;
        _abortBtn.Click += (s, e) => AbortAi();
        _cancelCurrentJobBtn = CreateButton("Cancel Job", Scale(78));
        _cancelCurrentJobBtn.ForeColor = Color.Salmon;
        _cancelCurrentJobBtn.Visible = false;
        _cancelCurrentJobBtn.Click += (s, e) => CancelAiJobForCurrentImage();

        aiToolsRow.Controls.Add(_overlayToggle);
        aiToolsRow.Controls.Add(_copyResultBtn);
        aiToolsRow.Controls.Add(_cancelCurrentJobBtn);
        aiToolsRow.Controls.Add(_abortBtn);

        var savedToggleRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = Scale(24),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _showSavedOcrCheck = new CheckBox
        {
            AutoSize = true,
            Text = "Show saved OCR",
            Checked = _settings.ImageViewerShowSavedOcr,
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 8),
            BackColor = Color.Transparent,
            Margin = new Padding(0, Scale(3), Scale(10), 0)
        };
        _showSavedTranslationCheck = new CheckBox
        {
            AutoSize = true,
            Text = "Show saved translation",
            Checked = _settings.ImageViewerShowSavedTranslation,
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 8),
            BackColor = Color.Transparent,
            Margin = new Padding(0, Scale(3), 0, 0)
        };
        _showSavedTranslationPreferred = _showSavedTranslationCheck.Checked;
        savedToggleRow.Controls.Add(_showSavedOcrCheck);
        savedToggleRow.Controls.Add(_showSavedTranslationCheck);

        var savedActionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = Scale(28),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _openSavedOcrFileBtn = CreateButton("Show", Scale(54));
        _deleteSavedTranslationBtn = CreateButton("Delete Translation", Scale(118));
        _clearOverlayBtn = CreateButton("Delete OCR", Scale(104));
        savedActionRow.Controls.Add(_openSavedOcrFileBtn);
        savedActionRow.Controls.Add(_deleteSavedTranslationBtn);
        savedActionRow.Controls.Add(_clearOverlayBtn);

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
        _aiPanel.Controls.Add(savedActionRow);
        _aiPanel.Controls.Add(savedToggleRow);
        _aiPanel.Controls.Add(_aiStatusLabel);
        _aiPanel.Controls.Add(aiToolsRow);
        _aiPanel.Controls.Add(reasoningRow);
        _aiPanel.Controls.Add(manualModeRow);
        _aiPanel.Controls.Add(contextHintRow);
        _aiPanel.Controls.Add(ocrHintRow);
        _aiPanel.Controls.Add(sourceHintRow);
        _aiPanel.Controls.Add(langRow);
        _aiPanel.Controls.Add(aiActionRow);
        _aiPanel.Visible = _settings.ImageViewerAiPanelVisible;

        _ocrBtn.Click += async (s, e) => await RunViewerOcrAsync(false);
        _translateBtn.Click += async (s, e) => await RunViewerOcrAsync(true);
        _drawOcrBoxBtn.Click += (s, e) => ToggleManualOcrDrawMode();
        _clearManualOcrBoxesBtn.Click += (s, e) => ClearPendingManualOcrRegions();
        _tagBtn.Click += async (s, e) => await RunViewerTaggingAsync();
        _targetLanguageBox.TextChanged += (s, e) => _settings.ImageViewerTargetLanguage = _targetLanguageBox.Text;
        _sourceLanguageHintBox.TextChanged += (s, e) => _settings.ImageViewerSourceLanguageHint = _sourceLanguageHintBox.Text;
        _ocrHintBox.TextChanged += (s, e) => _settings.ImageViewerOcrHint = _ocrHintBox.Text;
        _translationContextHintBox.TextChanged += (s, e) => _settings.ImageViewerTranslationContextHint = _translationContextHintBox.Text;
        _manualMaxEffortCheck.CheckedChanged += (s, e) =>
        {
            _settings.ImageViewerManualMaxEffortTranslation = _manualMaxEffortCheck.Checked;
            _settings.Save();
        };
        _overlayToggle.CheckedChanged += (s, e) =>
        {
            _settings.ImageViewerOverlayBoxesVisible = _overlayToggle.Checked;
            _settings.Save();
            _pictureBox.Invalidate();
        };
        _showSavedOcrCheck.CheckedChanged += (s, e) => OnShowSavedOcrToggled();
        _showSavedTranslationCheck.CheckedChanged += (s, e) => OnShowSavedTranslationToggled();
        _ocrReasoningCheck.CheckedChanged += (s, e) =>
        {
            _settings.ImageViewerOcrReasoningEnabled = _ocrReasoningCheck.Checked;
            _settings.Save();
        };
        _translationReasoningCheck.CheckedChanged += (s, e) =>
        {
            _settings.ImageViewerTranslationReasoningEnabled = _translationReasoningCheck.Checked;
            _settings.Save();
        };
        _clearOverlayBtn.Click += (s, e) => DeleteSavedOcrForCurrentImage();
        _deleteSavedTranslationBtn.Click += (s, e) => DeleteSavedTranslationForCurrentImage();
        _openSavedOcrFileBtn.Click += (s, e) => OpenSavedOcrFileForCurrentImage();
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
            if (IsImageFullyVisibleAtZoom(_zoomLevel))
                _panOffset = Point.Empty;
            _pictureBox.Invalidate();
        };

        _zoomInBtn = CreateButton("+", Scale(35));
        _zoomInBtn.Click += (s, e) => AdjustZoom(0.1f);

        _fitBtn = CreateButton("Fit", Scale(50));
        _fitBtn.Click += (s, e) => FitToWindow();

        _actualBtn = CreateButton("1:1", Scale(50));
        _actualBtn.Click += (s, e) => ActualSize();

        _rotateBtn = CreateButton("↻", Scale(38));
        _rotateBtn.Click += (s, e) => RotateImageClockwise();

        _fullscreenBtn = CreateButton("⛶", Scale(40));
        _fullscreenBtn.Click += (s, e) => ToggleFullscreen();

        _aiToggleBtn = CreateButton("AI", Scale(42));
        _aiToggleBtn.Click += (s, e) => ToggleAiPanel();

        // Add controls
        _controlPanel.Controls.AddRange(new Control[] { _prevBtn, _nextBtn, _infoContainer, _zoomOutBtn, _zoomSlider, _zoomInBtn, _zoomLabel, _fitBtn, _actualBtn, _rotateBtn, _fullscreenBtn, _aiToggleBtn });
        _controlPanel.Resize += (s, e) => LayoutControls();

        Controls.Add(_contentPanel);
        Controls.Add(_controlPanel);
        Controls.Add(_titleBar);

        // Custom Paint for Border
        Paint += (s, e) =>
        {
            if (_isFullscreen)
                return;

            using var p = new Pen(Color.FromArgb(60, 60, 60));
            e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        };

        LoadCurrentImage();
        ApplyAiPanelToggleVisualState();
        LayoutControls();
        ApplySavedWindowState();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (IsTextInputFocused())
            return base.ProcessCmdKey(ref msg, keyData);

        // Handle specific keys and combinations
        if (IsAnyHotkeyPressed(keyData, "ImageViewerPrevious", "ImageViewerPreviousAlt"))
        {
            ShowPrevious();
            return true;
        }
        if (IsAnyHotkeyPressed(keyData, "ImageViewerNext", "ImageViewerNextAlt", "ImageViewerNextSpace"))
        {
            ShowNext();
            return true;
        }
        if (IsHotkeyPressed("ImageViewerClose", keyData))
        {
            if (_isFullscreen) ToggleFullscreen(); else Close();
            return true;
        }
        if (IsAnyHotkeyPressed(keyData, "ImageViewerToggleFullscreen", "ImageViewerToggleFullscreenAlt"))
        {
            ToggleFullscreen();
            return true;
        }
        if (IsAnyHotkeyPressed(keyData, "ImageViewerZoomIn", "ImageViewerZoomInAlt"))
        {
            AdjustZoom(0.1f);
            return true;
        }
        if (IsAnyHotkeyPressed(keyData, "ImageViewerZoomOut", "ImageViewerZoomOutAlt"))
        {
            AdjustZoom(-0.1f);
            return true;
        }
        
        if (IsHotkeyPressed("ToggleOcrBoxes", keyData))
        {
            ToggleOverlayBoxes();
            return true;
        }
        if (IsHotkeyPressed("ToggleSavedTranslation", keyData))
        {
            ToggleSavedTranslation();
            return true;
        }
        if (IsHotkeyPressed("FitSmallDimension", keyData))
        {
            FitToWindowBySmallerDimension();
            return true;
        }
        if (IsHotkeyPressed("ImageViewerToggleAI", keyData))
        {
            ToggleAiPanel();
            return true;
        }
        if (IsHotkeyPressed("ImageViewerStartTranslation", keyData))
        {
            _ = RunViewerOcrAsync(true);
            return true;
        }
        if (IsHotkeyPressed("ImageViewerStartOcr", keyData))
        {
            _ = RunViewerOcrAsync(false);
            return true;
        }
        if (IsHotkeyPressed("ImageViewerTag", keyData))
        {
            _ = RunViewerTaggingAsync();
            return true;
        }
        if (IsHotkeyPressed("EditTags", keyData))
        {
            EditCurrentImageTags();
            return true;
        }
        if (IsHotkeyPressed("ImageViewerRotate", keyData))
        {
            RotateImageClockwise();
            return true;
        }

        if (IsAnyHotkeyPressed(keyData, "ImageViewerFitWindow", "ImageViewerFitWindowNumpad", "ImageViewerFitWindowPlain", "ImageViewerFitWindowPlainNumpad"))
        {
            FitToWindow();
            return true;
        }
        if (IsAnyHotkeyPressed(keyData, "ImageViewerActualSize", "ImageViewerActualSizeNumpad", "ImageViewerActualSizePlain", "ImageViewerActualSizePlainNumpad"))
        {
            ActualSize();
            return true;
        }
        if (IsAnyHotkeyPressed(keyData, "ImageViewerFitSmallDimensionControl", "ImageViewerFitSmallDimensionControlNumpad", "FitSmallDimension", "ImageViewerFitSmallDimensionNumpad"))
        {
            FitToWindowBySmallerDimension();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool IsTextInputFocused()
    {
        if (_aiOutputBox.Focused || ActiveControl == _aiOutputBox)
            return false;

        if (_targetLanguageBox.Focused ||
            _sourceLanguageHintBox.Focused ||
            _ocrHintBox.Focused ||
            _translationContextHintBox.Focused)
        {
            return true;
        }

        return ActiveControl is TextBoxBase or ComboBox or RichTextBox;
    }

    private void FocusViewerForHotkeys()
    {
        if (!IsTextInputFocused() && !_aiOutputBox.Focused)
            return;

        ActiveControl = null;
        Focus();
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
            if ((int)m.Result == HTCLIENT)
            {
                Point screenPoint = new Point(m.LParam.ToInt32());
                Point clientPoint = this.PointToClient(screenPoint);
                if (IsPointInCornerCloseHitZone(clientPoint))
                {
                    SetCornerCloseHover(true);
                    m.Result = (IntPtr)HTCLIENT;
                    return;
                }
                SetCornerCloseHover(false);

                if (this.WindowState != FormWindowState.Normal)
                    return;

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

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetCornerCloseHover(IsPointInCornerCloseHitZone(e.Location));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetCornerCloseHover(false);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && IsPointInCornerCloseHitZone(e.Location))
            Close();
    }

    private void SetCornerCloseHover(bool hover)
    {
        if (_cornerCloseHover == hover)
            return;

        _cornerCloseHover = hover;
        _closeBtn.BackColor = hover ? Color.FromArgb(232, 17, 35) : Color.Transparent;
        _closeBtn.ForeColor = ForeColor_Dark;
    }

    private bool IsPointInCornerCloseHitZone(Point clientPoint)
    {
        if (_isFullscreen)
            return false;

        int closeWidth = _closeBtn.Width > 0 ? _closeBtn.Width : Scale(46);
        int closeHeight = Math.Max(TitleBarHeight, _closeBtn.Height);
        return clientPoint.X >= Width - closeWidth - WindowFramePadding.Right &&
            clientPoint.Y <= closeHeight + WindowFramePadding.Top;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_autoFitEnabled && !_isFullscreen)
        {
            if (_autoFitBySmallerDimension)
                FitToWindowBySmallerDimension(allowUpscale: false);
            else
                FitToWindow(allowUpscale: false);
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

        _rotateBtn.Location = new Point(right - _rotateBtn.Width, centerButtonY);
        right = _rotateBtn.Left - spacing;

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

    private ContextMenuStrip BuildImageContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkToolStripRenderer(),
            ShowImageMargin = false,
            BackColor = Color.FromArgb(30, 30, 30)
        };
        _editOverlayBlockMenuItem = new ToolStripMenuItem("Edit OCR/Translation Box", null, (s, e) => EditContextOverlayBlock())
        {
            Enabled = false
        };
        menu.Items.Add(_editOverlayBlockMenuItem);
        menu.Items.Add("Set overlay defaults for this image", null, (s, e) => EditOverlayDefaults(perImage: true));
        menu.Items.Add("Set global overlay defaults", null, (s, e) => EditOverlayDefaults(perImage: false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Draw OCR Box", null, (s, e) => ToggleManualOcrDrawMode());
        menu.Items.Add("Clear Pending OCR Boxes", null, (s, e) => ClearPendingManualOcrRegions());
        menu.Items.Add("AI Tagging", null, async (s, e) => await RunViewerTaggingAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Localization.T("rotate_clockwise"), null, (s, e) => RotateImageClockwise());
        menu.Items.Add(Localization.T("edit_tags"), null, (s, e) => EditCurrentImageTags());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Copy Image File", null, (s, e) => CopyCurrentImageFileToClipboard());
        menu.Items.Add("Open File Location", null, (s, e) => OpenCurrentImageLocation());
        menu.Items.Add(Localization.T("properties"), null, (s, e) => ShowCurrentImageProperties());
        menu.Opening += (s, e) =>
        {
            _editOverlayBlockMenuItem.Enabled = _contextOverlayBlockIndex >= 0;
        };
        return menu;
    }

    private void EditOverlayDefaults(bool perImage)
    {
        if (perImage && string.IsNullOrWhiteSpace(GetCurrentImagePath()))
            return;

        OverlayStyleDefaults current = perImage
            ? _currentImageOverlayDefaults?.Clone() ?? new OverlayStyleDefaults()
            : GetGlobalOverlayDefaults();
        using var dialog = new OverlayStyleDefaultsDialog(
            current,
            perImage ? "Image Overlay Defaults" : "Global Overlay Defaults");
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        OverlayStyleDefaults updated = dialog.Settings;
        if (perImage)
            SaveOverlayDefaultsForCurrentImage(updated);
        else
            SaveGlobalOverlayDefaults(updated);
    }

    private void SaveGlobalOverlayDefaults(OverlayStyleDefaults style)
    {
        _settings.ImageViewerOverlayDefaultTextColorArgb = style.TextColorArgb;
        _settings.ImageViewerOverlayDefaultTextOutlineColorArgb = style.TextOutlineColorArgb;
        _settings.ImageViewerOverlayDefaultTextAlignment = FromStringAlignment(style.TextAlignment);
        _settings.ImageViewerOverlayDefaultTextVerticalAlignment = FromStringAlignment(style.TextVerticalAlignment);
        _settings.ImageViewerOverlayDefaultTextOutlineVisible = style.TextOutlineVisible;
        _settings.ImageViewerOverlayDefaultBoxFillColorArgb = style.BoxFillColorArgb;
        _settings.ImageViewerOverlayDefaultBoxFillVisible = style.BoxFillVisible;
        _settings.ImageViewerOverlayDefaultBoxBorderColorArgb = style.BoxBorderColorArgb;
        _settings.ImageViewerOverlayDefaultBoxBorderVisible = style.BoxBorderVisible;
        _settings.Save();
        _pictureBox.Invalidate();
    }

    private void SaveOverlayDefaultsForCurrentImage(OverlayStyleDefaults style)
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!TryGetExistingOcrCachePath(imagePath, out string cachePath))
        {
            if (_lastOcrResult == null)
            {
                RefreshAiStatusLabel("Run OCR before saving per-image overlay defaults");
                return;
            }

            SaveOcrResultToCache(imagePath, _settings.LlmModelName, _lastOcrResult);
            if (!TryGetExistingOcrCachePath(imagePath, out cachePath))
            {
                RefreshAiStatusLabel("Could not create the image OCR cache");
                return;
            }
        }

        if (!TryLoadSavedOcrEnvelope(imagePath, out var envelope) || envelope?.Result == null)
        {
            RefreshAiStatusLabel("Could not load the image OCR cache");
            return;
        }

        envelope.OverlayDefaults = style.IsEmpty ? null : style.Clone();
        File.WriteAllText(cachePath, SerializeOcrCacheEnvelopeForDisk(envelope));
        _currentImageOverlayDefaults = envelope.OverlayDefaults?.Clone();
        _pictureBox.Invalidate();
        RefreshAiStatusLabel("Saved image overlay defaults");
    }

    private int HitTestOverlayBlock(Point point)
    {
        if (!_overlayToggle.Checked || _overlayBlocks.Count == 0 || !TryGetCurrentImageDisplayRect(out var imageRect))
            return -1;

        for (int i = _overlayBlocks.Count - 1; i >= 0; i--)
        {
            var block = _overlayBlocks[i];
            var rect = new RectangleF(
                imageRect.X + (block.NormalizedRect.X * imageRect.Width),
                imageRect.Y + (block.NormalizedRect.Y * imageRect.Height),
                block.NormalizedRect.Width * imageRect.Width,
                block.NormalizedRect.Height * imageRect.Height);
            rect.Inflate(4f, 4f);
            if (rect.Contains(point))
                return i;
        }

        return -1;
    }

    private bool TryHitTestOverlayManipulation(Point point, out int blockIndex, out OverlayDragMode mode)
    {
        blockIndex = -1;
        mode = OverlayDragMode.None;

        if (!_overlayToggle.Checked || _overlayBlocks.Count == 0 || !TryGetCurrentImageDisplayRect(out var imageRect))
            return false;

        int edge = Math.Max(Scale(6), 4);
        for (int i = _overlayBlocks.Count - 1; i >= 0; i--)
        {
            var rect = GetOverlayBlockScreenRect(_overlayBlocks[i], imageRect);
            var hitRect = rect;
            hitRect.Inflate(edge, edge);
            if (!hitRect.Contains(point))
                continue;

            bool nearLeft = Math.Abs(point.X - rect.Left) <= edge;
            bool nearRight = Math.Abs(point.X - rect.Right) <= edge;
            bool nearTop = Math.Abs(point.Y - rect.Top) <= edge;
            bool nearBottom = Math.Abs(point.Y - rect.Bottom) <= edge;

            mode =
                nearLeft && nearTop ? OverlayDragMode.ResizeTopLeft :
                nearRight && nearTop ? OverlayDragMode.ResizeTopRight :
                nearLeft && nearBottom ? OverlayDragMode.ResizeBottomLeft :
                nearRight && nearBottom ? OverlayDragMode.ResizeBottomRight :
                nearLeft ? OverlayDragMode.ResizeLeft :
                nearRight ? OverlayDragMode.ResizeRight :
                nearTop ? OverlayDragMode.ResizeTop :
                nearBottom ? OverlayDragMode.ResizeBottom :
                OverlayDragMode.Move;

            blockIndex = i;
            return true;
        }

        return false;
    }

    private static RectangleF GetOverlayBlockScreenRect(OverlayTextBlock block, RectangleF imageRect)
        => new(
            imageRect.X + (block.NormalizedRect.X * imageRect.Width),
            imageRect.Y + (block.NormalizedRect.Y * imageRect.Height),
            block.NormalizedRect.Width * imageRect.Width,
            block.NormalizedRect.Height * imageRect.Height);

    private static Cursor GetOverlayDragCursor(OverlayDragMode mode)
        => mode switch
        {
            OverlayDragMode.Move => Cursors.SizeAll,
            OverlayDragMode.ResizeLeft or OverlayDragMode.ResizeRight => Cursors.SizeWE,
            OverlayDragMode.ResizeTop or OverlayDragMode.ResizeBottom => Cursors.SizeNS,
            OverlayDragMode.ResizeTopLeft or OverlayDragMode.ResizeBottomRight => Cursors.SizeNWSE,
            OverlayDragMode.ResizeTopRight or OverlayDragMode.ResizeBottomLeft => Cursors.SizeNESW,
            _ => Cursors.Default
        };

    private void EditContextOverlayBlock()
    {
        if (_contextOverlayBlockIndex < 0 || _contextOverlayBlockIndex >= _overlayBlocks.Count)
            return;

        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        var block = _overlayBlocks[_contextOverlayBlockIndex];
        int sourceIndex = block.SourceIndex;
        string originalSourceText = block.SourceText;
        string originalDisplayText = block.DisplayText;
        RectangleF originalRect = block.NormalizedRect;
        float originalFontSize = block.NormalizedFontSize;
        int? originalTextColorArgb = block.TextColorArgb;
        int? originalTextOutlineColorArgb = block.TextOutlineColorArgb;
        StringAlignment? originalTextAlignment = block.TextAlignment;
        StringAlignment? originalTextVerticalAlignment = block.TextVerticalAlignment;
        bool? originalTextOutlineVisible = block.TextOutlineVisible;
        int? originalBoxFillColorArgb = block.BoxFillColorArgb;
        int? originalBoxBorderColorArgb = block.BoxBorderColorArgb;
        bool? originalBoxFillVisible = block.BoxFillVisible;
        bool? originalBoxBorderVisible = block.BoxBorderVisible;
        bool originalHasUserOverride = block.HasUserOverride;
        string translationText = block.SourceIndex >= 0 && block.SourceIndex < _lastTranslations.Count
            ? _lastTranslations[block.SourceIndex]
            : "";
        bool preserveExplicitLineBreaks = block.HasUserOverride;

        using var dialog = new OverlayBlockEditDialog(
            preserveExplicitLineBreaks
                ? NormalizeEditedOverlayDisplayText(block.SourceText)
                : NormalizeOverlayDisplayText(block.SourceText),
            preserveExplicitLineBreaks
                ? NormalizeEditedOverlayDisplayText(translationText)
                : NormalizeOverlayDisplayText(translationText),
            block.NormalizedRect,
            block.NormalizedFontSize,
            block.TextColorArgb,
            block.TextOutlineColorArgb,
            block.TextAlignment,
            block.TextVerticalAlignment,
            block.TextOutlineVisible,
            block.BoxFillColorArgb,
            block.BoxBorderColorArgb,
            block.BoxFillVisible,
            block.BoxBorderVisible);

        dialog.PreviewChanged += (_, _) =>
        {
            PreviewOverlayBlockEdit(imagePath, sourceIndex, BuildOverlayBlockEditResult(dialog, trimText: false));
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            RestoreOverlayBlockPreview(
                imagePath,
                sourceIndex,
                originalSourceText,
                originalDisplayText,
                originalRect,
                originalFontSize,
                originalTextColorArgb,
                originalTextOutlineColorArgb,
                originalTextAlignment,
                originalTextVerticalAlignment,
                originalTextOutlineVisible,
                originalBoxFillColorArgb,
                originalBoxBorderColorArgb,
                originalBoxFillVisible,
                originalBoxBorderVisible,
                originalHasUserOverride);
            return;
        }

        ApplyOverlayBlockEdit(imagePath, sourceIndex, BuildOverlayBlockEditResult(dialog, trimText: true));
    }

    private static OverlayBlockEditResult BuildOverlayBlockEditResult(OverlayBlockEditDialog dialog, bool trimText)
    {
        string ocrText = trimText ? dialog.OcrText.Trim() : dialog.OcrText;
        string translationText = trimText ? dialog.TranslationText.Trim() : dialog.TranslationText;
        return new OverlayBlockEditResult
        {
            OcrText = ocrText,
            TranslationText = translationText,
            NormalizedRect = ClampNormalizedRect(
                dialog.NormalizedRect.X,
                dialog.NormalizedRect.Y,
                dialog.NormalizedRect.Width,
                dialog.NormalizedRect.Height),
            NormalizedFontSize = Math.Clamp(dialog.NormalizedFontSize, 0f, 0.5f),
            TextColorArgb = dialog.TextColorArgb,
            TextOutlineColorArgb = dialog.TextOutlineColorArgb,
            TextAlignment = dialog.TextAlignment,
            TextVerticalAlignment = dialog.TextVerticalAlignment,
            TextOutlineVisible = dialog.TextOutlineVisible,
            BoxFillColorArgb = dialog.BoxFillColorArgb,
            BoxBorderColorArgb = dialog.BoxBorderColorArgb,
            BoxFillVisible = dialog.BoxFillVisible,
            BoxBorderVisible = dialog.BoxBorderVisible,
            StyleSettingsChanged = dialog.StyleSettingsChanged
        };
    }

    private void PreviewOverlayBlockEdit(string imagePath, int sourceIndex, OverlayBlockEditResult edit)
    {
        if (!string.Equals(GetCurrentImagePath(), imagePath, StringComparison.OrdinalIgnoreCase))
            return;

        var block = _overlayBlocks.FirstOrDefault(b => b.SourceIndex == sourceIndex);
        if (block == null)
            return;

        block.SourceText = edit.OcrText;
        block.NormalizedRect = edit.NormalizedRect;
        block.NormalizedFontSize = edit.NormalizedFontSize;
        if (edit.StyleSettingsChanged)
        {
            block.TextColorArgb = edit.TextColorArgb;
            block.TextOutlineColorArgb = edit.TextOutlineColorArgb;
            block.TextAlignment = edit.TextAlignment;
            block.TextVerticalAlignment = edit.TextVerticalAlignment;
            block.TextOutlineVisible = edit.TextOutlineVisible;
            block.BoxFillColorArgb = edit.BoxFillColorArgb;
            block.BoxBorderColorArgb = edit.BoxBorderColorArgb;
            block.BoxFillVisible = edit.BoxFillVisible;
            block.BoxBorderVisible = edit.BoxBorderVisible;
        }
        block.HasUserOverride = true;
        string displayText =
            _showSavedTranslationCheck.Checked &&
            !string.IsNullOrWhiteSpace(edit.TranslationText)
                ? edit.TranslationText
                : edit.OcrText;
        block.DisplayText = NormalizeEditedOverlayDisplayText(displayText);
        _pictureBox.Invalidate();
    }

    private void RestoreOverlayBlockPreview(
        string imagePath,
        int sourceIndex,
        string sourceText,
        string displayText,
        RectangleF rect,
        float fontSize,
        int? textColorArgb,
        int? textOutlineColorArgb,
        StringAlignment? textAlignment,
        StringAlignment? textVerticalAlignment,
        bool? textOutlineVisible,
        int? boxFillColorArgb,
        int? boxBorderColorArgb,
        bool? boxFillVisible,
        bool? boxBorderVisible,
        bool hasUserOverride)
    {
        if (!string.Equals(GetCurrentImagePath(), imagePath, StringComparison.OrdinalIgnoreCase))
            return;

        var block = _overlayBlocks.FirstOrDefault(b => b.SourceIndex == sourceIndex);
        if (block == null)
            return;

        block.SourceText = sourceText;
        block.DisplayText = displayText;
        block.NormalizedRect = rect;
        block.NormalizedFontSize = fontSize;
        block.TextColorArgb = textColorArgb;
        block.TextOutlineColorArgb = textOutlineColorArgb;
        block.TextAlignment = textAlignment;
        block.TextVerticalAlignment = textVerticalAlignment;
        block.TextOutlineVisible = textOutlineVisible;
        block.BoxFillColorArgb = boxFillColorArgb;
        block.BoxBorderColorArgb = boxBorderColorArgb;
        block.BoxFillVisible = boxFillVisible;
        block.BoxBorderVisible = boxBorderVisible;
        block.HasUserOverride = hasUserOverride;
        _pictureBox.Invalidate();
    }

    private void ApplyOverlayBlockEdit(string imagePath, int sourceIndex, OverlayBlockEditResult edit)
    {
        if (sourceIndex < 0)
            return;

        if (!TryGetExistingOcrCachePath(imagePath, out string cachePath) && _lastOcrResult != null)
        {
            SaveOcrResultToCache(imagePath, _settings.LlmModelName, _lastOcrResult);
            TryGetExistingOcrCachePath(imagePath, out cachePath);
        }

        if (string.IsNullOrWhiteSpace(cachePath) ||
            !TryLoadSavedOcrEnvelope(imagePath, out var envelope) ||
            envelope?.Result == null)
        {
            RefreshAiStatusLabel("No saved OCR cache to edit");
            return;
        }

        envelope.OverlayOverrides ??= new List<OcrOverlayBlockOverride>();
        envelope.Result.Blocks ??= new List<LlmImageTextBlock>();
        if (sourceIndex >= envelope.Result.Blocks.Count)
            return;

        envelope.Result.Blocks[sourceIndex].Text = edit.OcrText;
        envelope.Result.FullText = ComposeFullTextFromBlocks(envelope.Result.Blocks);

        if (edit.TranslationText != null)
        {
            envelope.TranslationLines ??= new List<string>();
            while (envelope.TranslationLines.Count <= sourceIndex)
                envelope.TranslationLines.Add(string.Empty);
            envelope.TranslationLines[sourceIndex] = edit.TranslationText;
            envelope.TranslationFullText = string.Join(
                Environment.NewLine,
                envelope.TranslationLines.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
        }

        var existing = envelope.OverlayOverrides.FirstOrDefault(o => o.SourceIndex == sourceIndex);
        if (existing == null)
        {
            existing = new OcrOverlayBlockOverride { SourceIndex = sourceIndex };
            envelope.OverlayOverrides.Add(existing);
        }

        existing.Text = edit.OcrText;
        if (edit.TranslationText != null)
            existing.TranslationText = edit.TranslationText;
        existing.X = edit.NormalizedRect.X;
        existing.Y = edit.NormalizedRect.Y;
        existing.W = edit.NormalizedRect.Width;
        existing.H = edit.NormalizedRect.Height;
        existing.FontSize = edit.NormalizedFontSize;
        if (edit.StyleSettingsChanged)
        {
            existing.TextColorArgb = edit.TextColorArgb;
            existing.TextOutlineColorArgb = edit.TextOutlineColorArgb;
            existing.TextAlignment = edit.TextAlignment;
            existing.TextVerticalAlignment = edit.TextVerticalAlignment;
            existing.TextOutlineVisible = edit.TextOutlineVisible;
            existing.BoxFillColorArgb = edit.BoxFillColorArgb;
            existing.BoxBorderColorArgb = edit.BoxBorderColorArgb;
            existing.BoxFillVisible = edit.BoxFillVisible;
            existing.BoxBorderVisible = edit.BoxBorderVisible;
        }

        File.WriteAllText(cachePath, SerializeOcrCacheEnvelopeForDisk(envelope));

        _lastOcrResult = CloneOcrResult(envelope.Result);
        _savedTranslationForCurrentImage = TryBuildSavedTranslation(envelope, out var savedTranslation)
            ? savedTranslation
            : null;
        _lastTranslations = _savedTranslationForCurrentImage?.Translations?.ToList() ?? new List<string>();

        SetOverlayFromOcrResult(_lastOcrResult, _showSavedTranslationCheck.Checked ? _lastTranslations : null);
        ApplyCachedOverlayOverridesForCurrentImage();
        _aiOutputBox.Text = _savedTranslationForCurrentImage != null && _showSavedTranslationCheck.Checked
            ? RenderTranslatedResult(_lastOcrResult, _savedTranslationForCurrentImage)
            : RenderOcrResult(_lastOcrResult);
        RefreshAiStatusLabel("Saved OCR box edit");
        UpdateSavedCacheUiState();
    }

    private void SaveOverlayBlockDragEdit()
    {
        if (_overlayDragBlockIndex < 0 || _overlayDragBlockIndex >= _overlayBlocks.Count)
            return;

        string? imagePath = _overlayDragImagePath;
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!string.Equals(GetCurrentImagePath(), imagePath, StringComparison.OrdinalIgnoreCase))
            return;

        var block = _overlayBlocks[_overlayDragBlockIndex];
        string? translationText = null;
        if (block.SourceIndex >= 0 && block.SourceIndex < _lastTranslations.Count)
        {
            translationText = _lastTranslations[block.SourceIndex];
        }
        else if (TryLoadSavedOcrEnvelope(imagePath, out var savedEnvelope) &&
                 savedEnvelope?.TranslationLines != null &&
                 block.SourceIndex >= 0 &&
                 block.SourceIndex < savedEnvelope.TranslationLines.Count)
        {
            // Dragging must not turn an incomplete in-memory translation list into an
            // empty cache entry. This also protects against older caches created before
            // translation placeholders were preserved.
            translationText = savedEnvelope.TranslationLines[block.SourceIndex] ?? "";
        }
        else
        {
            // Leave it null so ApplyOverlayBlockEdit preserves the cache value when the
            // in-memory translation list cannot identify this block.
        }

        Func<string, string> normalizeForPersistence = _overlayDragStartHadUserOverride
            ? NormalizeEditedOverlayDisplayText
            : NormalizeOverlayDisplayText;

        ApplyOverlayBlockEdit(imagePath, block.SourceIndex, new OverlayBlockEditResult
        {
            OcrText = normalizeForPersistence(block.SourceText),
            TranslationText = translationText == null
                ? null
                : normalizeForPersistence(translationText),
            NormalizedRect = block.NormalizedRect,
            NormalizedFontSize = block.NormalizedFontSize
        });
    }

    private void CancelOverlayDrag(bool invalidate = true)
    {
        if (_overlayDragMode == OverlayDragMode.None &&
            _overlayDragBlockIndex < 0 &&
            string.IsNullOrWhiteSpace(_overlayDragImagePath))
        {
            return;
        }

        _overlayDragMode = OverlayDragMode.None;
        _overlayDragBlockIndex = -1;
        _overlayDragImagePath = null;
        _overlayDragStartHadUserOverride = false;
        _overlayDragChanged = false;
        _pictureBox.Cursor = Cursors.Default;
        if (invalidate)
            _pictureBox.Invalidate();
    }

    private void EditCurrentImageTags()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        var currentTags = TagManager.Instance.GetTags(imagePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var dlg = new EditTagsForm(string.Join(", ", currentTags));
        dlg.Text = Localization.T("edit_tags_title");

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        var finalTags = dlg.TagsResult
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (dlg.ClearAllRequested)
        {
            TagManager.Instance.SetTagsBatch(new[] { imagePath }, finalTags);
        }
        else
        {
            var toAdd = finalTags.Where(t => !currentTags.Contains(t)).ToList();
            var toRemove = currentTags.Where(t => !finalTags.Contains(t)).ToList();
            TagManager.Instance.UpdateTagsBatch(new[] { imagePath }, toAdd, toRemove);
        }

        UpdateTags(imagePath);
    }

    private void CopyCurrentImageFileToClipboard()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        try
        {
            var files = new StringCollection { imagePath };
            Clipboard.SetFileDropList(files);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CopyCurrentImageFileToClipboard failed: {ex.Message}");
        }
    }

    private void OpenCurrentImageLocation()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        string selectArg = $"/select,\"{imagePath}\"";
        try
        {
            var existingMain = Application.OpenForms
                .OfType<MainForm>()
                .LastOrDefault(f => !f.IsDisposed);
            if (existingMain != null)
            {
                if (existingMain.WindowState == FormWindowState.Minimized)
                    existingMain.WindowState = FormWindowState.Normal;
                existingMain.Show();
                existingMain.Activate();
                existingMain.BringToFront();
                existingMain.HandleExternalPathNoViewer(selectArg);
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenCurrentImageLocation via existing MainForm failed: {ex.Message}");
        }

        try
        {
            string directory = Path.GetDirectoryName(imagePath) ?? imagePath;
            string exePath = Application.ExecutablePath;
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"\"{directory}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenCurrentImageLocation via app launch failed: {ex.Message}");
        }
    }

    private void ShowCurrentImageProperties()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        try
        {
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask = SEE_MASK_INVOKEIDLIST,
                hwnd = Handle,
                lpVerb = "properties",
                lpFile = imagePath,
                lpParameters = string.Empty,
                lpDirectory = string.Empty,
                nShow = 5
            };
            _ = ShellExecuteEx(ref info);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ShowCurrentImageProperties failed: {ex.Message}");
        }
    }

    private void ToggleAiPanel()
    {
        _aiPanel.Visible = !_aiPanel.Visible;
        _settings.ImageViewerAiPanelVisible = _aiPanel.Visible;
        _settings.Save();
        ApplyAiPanelToggleVisualState();
        _contentPanel.PerformLayout();
        LayoutControls();
        _pictureBox.Invalidate();
    }

    private void ApplyAiPanelToggleVisualState()
    {
        _aiToggleBtn.BackColor = _aiPanel.Visible ? Color.FromArgb(78, 78, 78) : Color.FromArgb(60, 60, 60);
        _aiToggleBtn.ForeColor = _aiPanel.Visible ? Color.White : ForeColor_Dark;
    }

    private void ToggleManualOcrDrawMode()
    {
        _manualOcrDrawMode = !_manualOcrDrawMode;
        _isDrawingManualOcrRegion = false;
        UpdateManualOcrUiState();
        _pictureBox.Invalidate();
        RefreshAiStatusLabel(_manualOcrDrawMode ? "Draw OCR boxes with the mouse" : null);
    }

    private void ClearPendingManualOcrRegions(bool updateStatus = true)
    {
        _pendingManualOcrRegions.Clear();
        _isDrawingManualOcrRegion = false;
        UpdateManualOcrUiState();
        _pictureBox.Invalidate();

        if (updateStatus)
            RefreshAiStatusLabel("Cleared pending manual OCR boxes");
    }

    private void UpdateManualOcrUiState()
    {
        bool canEdit = _currentImage != null && !IsCurrentImageActivelyProcessing();
        _drawOcrBoxBtn.Enabled = canEdit;
        _clearManualOcrBoxesBtn.Enabled = canEdit && _pendingManualOcrRegions.Count > 0;
        _drawOcrBoxBtn.BackColor = _manualOcrDrawMode ? Color.FromArgb(78, 78, 78) : Color.FromArgb(60, 60, 60);
        _drawOcrBoxBtn.ForeColor = _manualOcrDrawMode ? Color.White : ForeColor_Dark;
        _pictureBox.Cursor = _manualOcrDrawMode ? Cursors.Cross : Cursors.Default;
    }

    private void SetAiBusy(bool busy, string statusText)
    {
        _aiBusy = busy;
        UpdateAiActionControlsState();
        _targetLanguageBox.Enabled = _currentImage != null;
        _sourceLanguageHintBox.Enabled = _currentImage != null;
        _ocrHintBox.Enabled = _currentImage != null;
        _translationContextHintBox.Enabled = _currentImage != null;
        _ocrReasoningCheck.Enabled = _currentImage != null;
        _translationReasoningCheck.Enabled = _currentImage != null;
        _abortBtn.Visible = busy;
        UpdateCancelCurrentJobButton();
        _overlayToggle.Enabled = true;
        _showSavedOcrCheck.Enabled = _currentImage != null;
        _copyResultBtn.Enabled = true;
        _aiStatusLabel.Text = statusText;
        if (busy)
            _aiOutputBox.Cursor = Cursors.WaitCursor;
        else
            _aiOutputBox.Cursor = Cursors.Default;
        UpdateManualOcrUiState();
        UpdateSavedCacheUiState();
    }

    private void UpdateCancelCurrentJobButton()
    {
        bool canCancel = TryGetCancelableAiJobForCurrentImage(out _);
        _cancelCurrentJobBtn.Visible = canCancel;
        _cancelCurrentJobBtn.Enabled = canCancel;
    }

    private bool HasQueuedAiWork()
        => _activeAiJob != null || _queuedAiJobs.Count > 0;

    private bool IsCurrentImageActivelyProcessing()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return false;

        if (_activeAiJob != null &&
            string.Equals(_activeAiJob.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(_activeTagImagePath) &&
            string.Equals(_activeTagImagePath, imagePath, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCurrentImageOverlayJobPending()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return false;

        return (_activeAiJob != null &&
                string.Equals(_activeAiJob.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase)) ||
            GetQueuedAiJobsForImage(imagePath) > 0;
    }

    private void UpdateAiActionControlsState()
    {
        bool currentImageActive = IsCurrentImageActivelyProcessing();
        _ocrBtn.Enabled = _currentImage != null && !currentImageActive;
        _translateBtn.Enabled = _currentImage != null && !currentImageActive;
        _tagBtn.Enabled = _currentImage != null && !currentImageActive && !_aiBusy && _tagCts == null;
    }

    private int GetQueuedAiJobsForImage(string imagePath)
        => _queuedAiJobCountsByImage.TryGetValue(imagePath, out int count) ? count : 0;

    private bool TryGetCancelableAiJobForCurrentImage(out ImageAiJob? job)
    {
        job = null;
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return false;

        if (_activeAiJob != null &&
            string.Equals(_activeAiJob.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
        {
            job = _activeAiJob;
            return true;
        }

        job = _queuedAiJobs.FirstOrDefault(queued =>
            string.Equals(queued.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase));
        return job != null;
    }

    private void IncrementQueuedAiJobsForImage(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        _queuedAiJobCountsByImage[imagePath] = GetQueuedAiJobsForImage(imagePath) + 1;
    }

    private void DecrementQueuedAiJobsForImage(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        int count = GetQueuedAiJobsForImage(imagePath);
        if (count <= 1)
            _queuedAiJobCountsByImage.Remove(imagePath);
        else
            _queuedAiJobCountsByImage[imagePath] = count - 1;
    }

    private void RegisterQueuedManualRegions(ImageAiJob job)
    {
        if (job.ManualSnippets.Count == 0 || string.IsNullOrWhiteSpace(job.ImagePath))
            return;

        if (!_queuedManualRegionsByImage.TryGetValue(job.ImagePath, out var regions))
        {
            regions = new List<RectangleF>();
            _queuedManualRegionsByImage[job.ImagePath] = regions;
        }

        regions.AddRange(job.ManualSnippets.Select(s => s.NormalizedRect));
    }

    private void UnregisterQueuedManualRegions(ImageAiJob job)
    {
        if (job.ManualSnippets.Count == 0 || string.IsNullOrWhiteSpace(job.ImagePath))
            return;

        if (!_queuedManualRegionsByImage.TryGetValue(job.ImagePath, out var regions) || regions.Count == 0)
            return;

        foreach (var snippet in job.ManualSnippets)
        {
            int index = regions.FindIndex(r =>
                Math.Abs(r.X - snippet.NormalizedRect.X) < 0.0001f &&
                Math.Abs(r.Y - snippet.NormalizedRect.Y) < 0.0001f &&
                Math.Abs(r.Width - snippet.NormalizedRect.Width) < 0.0001f &&
                Math.Abs(r.Height - snippet.NormalizedRect.Height) < 0.0001f);
            if (index >= 0)
                regions.RemoveAt(index);
        }

        if (regions.Count == 0)
            _queuedManualRegionsByImage.Remove(job.ImagePath);
    }

    private static bool RectanglesRoughlyEqual(RectangleF a, RectangleF b)
        => Math.Abs(a.X - b.X) < 0.0001f &&
           Math.Abs(a.Y - b.Y) < 0.0001f &&
           Math.Abs(a.Width - b.Width) < 0.0001f &&
           Math.Abs(a.Height - b.Height) < 0.0001f;

    private static void AddRectIfMissing(List<RectangleF> regions, RectangleF rect)
    {
        if (!regions.Any(existing => RectanglesRoughlyEqual(existing, rect)))
            regions.Add(rect);
    }

    private void AddPendingManualRegionIfMissing(RectangleF rect)
    {
        if (!_pendingManualOcrRegions.Any(existing => RectanglesRoughlyEqual(existing.NormalizedRect, rect)))
            _pendingManualOcrRegions.Add(new ManualOcrRegion { NormalizedRect = rect });
    }

    private bool HasQueuedManualRegions(ImageAiJob job)
    {
        if (job.ManualSnippets.Count == 0 || string.IsNullOrWhiteSpace(job.ImagePath))
            return false;

        if (!_queuedManualRegionsByImage.TryGetValue(job.ImagePath, out var regions) || regions.Count == 0)
            return false;

        return job.ManualSnippets.Any(snippet => regions.Any(region => RectanglesRoughlyEqual(region, snippet.NormalizedRect)));
    }

    private void RestoreManualRegionsFromAbortedJob(ImageAiJob job)
    {
        if (!HasQueuedManualRegions(job))
            return;

        string? currentImagePath = GetCurrentImagePath();
        bool isCurrentImage = string.Equals(currentImagePath, job.ImagePath, StringComparison.OrdinalIgnoreCase);
        List<RectangleF>? storedRegions = null;
        if (!isCurrentImage)
        {
            if (!_restorableManualRegionsByImage.TryGetValue(job.ImagePath, out storedRegions))
            {
                storedRegions = new List<RectangleF>();
                _restorableManualRegionsByImage[job.ImagePath] = storedRegions;
            }
        }

        foreach (var snippet in job.ManualSnippets)
        {
            if (isCurrentImage)
                AddPendingManualRegionIfMissing(snippet.NormalizedRect);
            else
                AddRectIfMissing(storedRegions!, snippet.NormalizedRect);
        }
    }

    private void RestorePendingManualRegionsForCurrentImage()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!_restorableManualRegionsByImage.TryGetValue(imagePath, out var restoredRegions) || restoredRegions.Count == 0)
            return;

        foreach (var rect in restoredRegions)
            AddPendingManualRegionIfMissing(rect);

        _restorableManualRegionsByImage.Remove(imagePath);
    }

    private void RefreshAiStatusLabel(string? overrideStatus = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideStatus))
        {
            _aiStatusLabel.Text = overrideStatus;
            return;
        }

        if (_manualOcrDrawMode)
        {
            _aiStatusLabel.Text = "Draw OCR boxes with the mouse";
            return;
        }

        if (_pendingManualOcrRegions.Count > 0)
        {
            _aiStatusLabel.Text = $"{_pendingManualOcrRegions.Count} manual OCR box(es) queued";
            return;
        }

        string? imagePath = GetCurrentImagePath();
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            if (_activeAiJob != null &&
                string.Equals(_activeAiJob.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
            {
                string action = _activeAiJob.WithTranslation ? "translate" : "OCR";
                _aiStatusLabel.Text = _activeAiJob.ManualSnippets.Count > 0
                    ? $"Processing queued manual {action}..."
                    : $"Processing queued {action}...";
                return;
            }

            int queuedForImage = GetQueuedAiJobsForImage(imagePath);
            if (queuedForImage > 0)
            {
                _aiStatusLabel.Text = queuedForImage == 1
                    ? "1 AI job queued for this image"
                    : $"{queuedForImage} AI jobs queued for this image";
                return;
            }
        }

        if (HasQueuedAiWork())
        {
            int pending = _queuedAiJobs.Count + (_activeAiJob != null ? 1 : 0);
            _aiStatusLabel.Text = pending == 1 ? "1 AI job in progress" : $"{pending} AI jobs in progress";
            return;
        }

        _aiStatusLabel.Text = "AI ready";
    }

    private async Task<string?> EnsureVisionModelAsync()
    {
        _llmService.ApiUrl = LlmService.GetCompletionsApiUrl(_settings.LlmApiUrl, null);
        return await _llmService.ResolveModelForTaskAsync(LlmUsageKind.Assistant, LlmTaskKind.Vision, this);
    }

    private static string GetOcrOutputDirectory()
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OCR_output");

    private static string ComputeNormalizedPathHash(string imagePath)
    {
        string normalized = Path.GetFullPath(imagePath).Trim().ToLowerInvariant();
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes);
    }

    private static string SanitizeFileComponent(string value, int maxLen = 64)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "image";

        var sb = new StringBuilder(value.Length);
        foreach (char ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
                sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '.')
                sb.Append('_');
        }

        string sanitized = sb.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "image";
        if (sanitized.Length > maxLen)
            sanitized = sanitized.Substring(0, maxLen);
        return sanitized;
    }

    private static string GetOcrCachePath(string imagePath)
    {
        string hash = ComputeNormalizedPathHash(imagePath);
        string imageName = SanitizeFileComponent(Path.GetFileNameWithoutExtension(imagePath));
        string shortHash = hash.Length > 12 ? hash.Substring(0, 12) : hash;
        return Path.Combine(GetOcrOutputDirectory(), $"{imageName}__{shortHash}.json");
    }

    private static string GetLegacyOcrCachePath(string imagePath)
    {
        string hash = ComputeNormalizedPathHash(imagePath);
        return Path.Combine(GetOcrOutputDirectory(), $"{hash}.json");
    }

    private static IEnumerable<string> EnumerateOcrCacheCandidates(string imagePath)
    {
        yield return GetOcrCachePath(imagePath);
        yield return GetLegacyOcrCachePath(imagePath);
    }

    private static bool TryGetExistingOcrCachePath(string imagePath, out string cachePath)
    {
        foreach (var candidate in EnumerateOcrCacheCandidates(imagePath))
        {
            if (File.Exists(candidate))
            {
                cachePath = candidate;
                return true;
            }
        }

        cachePath = GetOcrCachePath(imagePath);
        return false;
    }

    private static bool TryGetSourceStamp(string imagePath, out long length, out long lastWriteUtcTicks)
    {
        length = 0;
        lastWriteUtcTicks = 0;

        try
        {
            var info = new FileInfo(imagePath);
            if (!info.Exists)
                return false;

            length = info.Length;
            lastWriteUtcTicks = info.LastWriteTimeUtc.Ticks;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static OcrCacheEnvelope? TryReadOcrEnvelopeUnchecked(string cachePath)
    {
        try
        {
            if (!File.Exists(cachePath))
                return null;

            string raw = File.ReadAllText(cachePath);
            string json = ExtractJsonPayload(raw);
            var envelope = JsonSerializer.Deserialize<OcrCacheEnvelope>(json);
            if (envelope == null)
                return null;

            envelope.TranslationLines ??= new List<string>();
            envelope.OverlayOverrides ??= new List<OcrOverlayBlockOverride>();
            return envelope;
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractJsonPayload(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        int separatorIndex = raw.IndexOf(OcrCacheJsonSeparator, StringComparison.Ordinal);
        if (separatorIndex >= 0)
        {
            int jsonStart = separatorIndex + OcrCacheJsonSeparator.Length;
            while (jsonStart < raw.Length && (raw[jsonStart] == '\r' || raw[jsonStart] == '\n' || char.IsWhiteSpace(raw[jsonStart])))
                jsonStart++;
            if (jsonStart < raw.Length)
                return raw.Substring(jsonStart);
        }

        int firstBrace = raw.IndexOf('{');
        if (firstBrace > 0)
            return raw.Substring(firstBrace);

        return raw;
    }

    private static string BuildCleanOcrTextBlock(OcrCacheEnvelope envelope)
    {
        string ocrText = envelope.Result?.FullText?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(ocrText))
        {
            ocrText = string.Join(
                Environment.NewLine,
                envelope.Result?.Blocks?
                    .Select(b => b?.Text?.Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Cast<string>() ?? Array.Empty<string>());
        }

        string translated = envelope.TranslationFullText?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(translated) && envelope.TranslationLines != null && envelope.TranslationLines.Count > 0)
        {
            translated = string.Join(
                Environment.NewLine,
                envelope.TranslationLines
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim()));
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(ocrText))
            sb.AppendLine(ocrText);
        if (!string.IsNullOrWhiteSpace(translated))
        {
            if (sb.Length > 0)
                sb.AppendLine();
            sb.AppendLine(translated);
        }

        return sb.ToString().TrimEnd();
    }

    private static string SerializeOcrCacheEnvelopeForDisk(OcrCacheEnvelope envelope)
    {
        string json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        string cleanText = BuildCleanOcrTextBlock(envelope);
        if (string.IsNullOrWhiteSpace(cleanText))
            return $"{OcrCacheJsonSeparator}{Environment.NewLine}{json}";

        return $"{cleanText}{Environment.NewLine}{Environment.NewLine}{OcrCacheJsonSeparator}{Environment.NewLine}{json}";
    }

    private static bool TryLoadSavedOcrEnvelope(string imagePath, out OcrCacheEnvelope? envelope)
    {
        envelope = null;

        if (!TryGetSourceStamp(imagePath, out long srcLength, out long srcTicks))
            return false;

        if (!TryGetExistingOcrCachePath(imagePath, out string cachePath))
            return false;

        var loaded = TryReadOcrEnvelopeUnchecked(cachePath);
        if (loaded?.Result == null)
            return false;

        if (loaded.SourceLength != srcLength || loaded.SourceLastWriteUtcTicks != srcTicks)
            return false;

        if (string.IsNullOrWhiteSpace(loaded.Result.FullText) && (loaded.Result.Blocks == null || loaded.Result.Blocks.Count == 0))
            return false;

        loaded.Result.Blocks ??= new List<LlmImageTextBlock>();
        loaded.TranslationLines ??= new List<string>();
        envelope = loaded;
        return true;
    }

    private static bool TryBuildSavedTranslation(OcrCacheEnvelope envelope, out LlmTextTranslationResult? translation)
    {
        translation = null;
        var lines = envelope.TranslationLines ?? new List<string>();
        bool hasLines = lines.Any(t => !string.IsNullOrWhiteSpace(t));
        bool hasFull = !string.IsNullOrWhiteSpace(envelope.TranslationFullText);
        if (!hasLines && !hasFull)
            return false;

        // Keep empty entries so translation index N continues to refer to OCR block N.
        // Filtering them here shifts every following translation onto the wrong box.
        var normalized = lines
            .Select(t => t?.Trim() ?? "")
            .ToList();

        translation = new LlmTextTranslationResult
        {
            TargetLanguage = string.IsNullOrWhiteSpace(envelope.TranslationTargetLanguage) ? "Unknown" : envelope.TranslationTargetLanguage,
            TranslatedFullText = envelope.TranslationFullText ?? "",
            Translations = normalized
        };

        if (translation.Translations.Count == 0 && !string.IsNullOrWhiteSpace(translation.TranslatedFullText))
            translation.Translations = new List<string> { translation.TranslatedFullText };
        if (string.IsNullOrWhiteSpace(translation.TranslatedFullText) && translation.Translations.Count > 0)
            translation.TranslatedFullText = string.Join(Environment.NewLine, translation.Translations);

        return true;
    }

    private static bool TryLoadSavedTranslationResult(string imagePath, out LlmTextTranslationResult? translation)
    {
        translation = null;
        if (!TryLoadSavedOcrEnvelope(imagePath, out var envelope) || envelope == null)
            return false;

        return TryBuildSavedTranslation(envelope, out translation);
    }

    private static string NormalizeLanguageKey(string? language)
        => string.IsNullOrWhiteSpace(language) ? "" : language.Trim().ToLowerInvariant();

    private void ApplyLoadedOcrToViewer(string imagePath, LlmImageTextResult ocr, bool fromSavedCache)
    {
        _ocrImagePath = imagePath;
        _lastOcrResult = ocr;
        _lastTranslations = new List<string>();
        SetOverlayFromOcrResult(ocr, null);
        ApplyCachedOverlayOverridesForCurrentImage();
        _aiOutputBox.Text = RenderOcrResult(ocr);
        _currentOverlayFromSavedCache = fromSavedCache;

        if (!fromSavedCache)
            return;

        string cacheLabel = TryGetExistingOcrCachePath(imagePath, out string existingCachePath)
            ? Path.GetFileName(existingCachePath)
            : Path.GetFileName(GetOcrCachePath(imagePath));
        _aiOutputBox.AppendText(Environment.NewLine + Environment.NewLine + $"[Loaded from OCR_output cache: {cacheLabel}]");
    }

    private static void SaveOcrResultToCache(string imagePath, string? modelId, LlmImageTextResult ocr)
    {
        if (ocr == null)
            return;

        if (!TryGetSourceStamp(imagePath, out long srcLength, out long srcTicks))
            return;

        try
        {
            Directory.CreateDirectory(GetOcrOutputDirectory());
            string cachePath = GetOcrCachePath(imagePath);
            var envelope = TryReadOcrEnvelopeUnchecked(cachePath) ?? new OcrCacheEnvelope();

            envelope.SourcePath = Path.GetFullPath(imagePath);
            envelope.SourceLength = srcLength;
            envelope.SourceLastWriteUtcTicks = srcTicks;
            envelope.SavedUtcTicks = DateTime.UtcNow.Ticks;
            if (!string.IsNullOrWhiteSpace(modelId))
                envelope.ModelId = modelId!;
            envelope.Result = ocr;
            envelope.TranslationTargetLanguage = "";
            envelope.TranslationSourceLanguage = "";
            envelope.TranslationModelId = "";
            envelope.TranslationFullText = "";
            envelope.TranslationLines = new List<string>();
            envelope.TranslationSavedUtcTicks = 0;

            File.WriteAllText(cachePath, SerializeOcrCacheEnvelopeForDisk(envelope));
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to save OCR cache for '{imagePath}': {ex.Message}");
        }
    }

    private static void SaveTranslationToCache(string imagePath, string? modelId, LlmImageTextResult ocr, LlmTextTranslationResult translation)
    {
        if (ocr == null || translation == null)
            return;

        if (!TryGetSourceStamp(imagePath, out long srcLength, out long srcTicks))
            return;

        try
        {
            Directory.CreateDirectory(GetOcrOutputDirectory());
            string cachePath = GetOcrCachePath(imagePath);
            var envelope = TryReadOcrEnvelopeUnchecked(cachePath) ?? new OcrCacheEnvelope();

            envelope.SourcePath = Path.GetFullPath(imagePath);
            envelope.SourceLength = srcLength;
            envelope.SourceLastWriteUtcTicks = srcTicks;
            envelope.SavedUtcTicks = DateTime.UtcNow.Ticks;
            if (!string.IsNullOrWhiteSpace(modelId))
            {
                envelope.ModelId = modelId!;
                envelope.TranslationModelId = modelId!;
            }
            envelope.Result = ocr;
            envelope.TranslationTargetLanguage = translation.TargetLanguage ?? "";
            envelope.TranslationSourceLanguage = ocr.DetectedLanguage ?? "";
            envelope.TranslationFullText = translation.TranslatedFullText ?? "";
            // TranslationLines is parallel to OCR blocks; empty translations must remain
            // as placeholders instead of shifting later entries to earlier boxes.
            envelope.TranslationLines = translation.Translations?
                .Select(t => t?.Trim() ?? "")
                .ToList() ?? new List<string>();
            envelope.TranslationSavedUtcTicks = DateTime.UtcNow.Ticks;

            if (string.IsNullOrWhiteSpace(envelope.TranslationFullText) && envelope.TranslationLines.Count > 0)
                envelope.TranslationFullText = string.Join(Environment.NewLine, envelope.TranslationLines);

            File.WriteAllText(cachePath, SerializeOcrCacheEnvelopeForDisk(envelope));
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to save translation cache for '{imagePath}': {ex.Message}");
        }
    }

    private void SetShowSavedTranslationChecked(bool value, bool updatePreference = false)
    {
        if (updatePreference)
        {
            _showSavedTranslationPreferred = value;
            _settings.ImageViewerShowSavedTranslation = value;
            _settings.Save();
        }

        _suppressSavedTranslationToggleEvent = true;
        try
        {
            _showSavedTranslationCheck.Checked = value;
        }
        finally
        {
            _suppressSavedTranslationToggleEvent = false;
        }
    }

    private void UpdateSavedCacheUiState()
    {
        bool hasSaved = false;
        bool hasSavedTranslation = false;
        string? imagePath = GetCurrentImagePath();
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            hasSaved = TryGetExistingOcrCachePath(imagePath, out _);
            hasSavedTranslation =
                TryLoadSavedOcrEnvelope(imagePath, out var envelope) &&
                envelope != null &&
                TryBuildSavedTranslation(envelope, out _);
        }

        bool currentImageActive = IsCurrentImageActivelyProcessing();
        _openSavedOcrFileBtn.Enabled = hasSaved;
        _clearOverlayBtn.Enabled = hasSaved && !currentImageActive;
        _deleteSavedTranslationBtn.Enabled = hasSavedTranslation && !currentImageActive;
        _showSavedOcrCheck.Enabled = _currentImage != null;

        _showSavedTranslationCheck.Enabled = _showSavedOcrCheck.Checked && _savedTranslationForCurrentImage != null;
    }

    private void OnShowSavedTranslationToggled()
    {
        if (!_suppressSavedTranslationToggleEvent)
        {
            _showSavedTranslationPreferred = _showSavedTranslationCheck.Checked;
            _settings.ImageViewerShowSavedTranslation = _showSavedTranslationCheck.Checked;
            _settings.Save();
        }

        ApplySavedTranslationToggleForCurrentImage();
    }

    private void OnShowSavedOcrToggled()
    {
        _settings.ImageViewerShowSavedOcr = _showSavedOcrCheck.Checked;
        _settings.Save();

        if (_showSavedOcrCheck.Checked)
        {
            TryApplySavedOcrForCurrentImage(allowStatusUpdate: true);
        }
        else
        {
            if (_currentOverlayFromSavedCache)
            {
                _overlayBlocks.Clear();
                _lastTranslations = new List<string>();
                _savedTranslationForCurrentImage = null;
                _ocrImagePath = null;
                _lastOcrResult = null;
                _aiOutputBox.Clear();
                _pictureBox.Invalidate();
                _currentOverlayFromSavedCache = false;
            }
            if (!_aiBusy)
                _aiStatusLabel.Text = "Saved OCR hidden";
        }

        UpdateSavedCacheUiState();
    }

    private void DeleteSavedOcrForCurrentImage()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        int deleted = 0;
        foreach (var path in EnumerateOcrCacheCandidates(imagePath))
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                LlmDebugLogger.LogError($"Failed to delete saved OCR file '{path}': {ex.Message}");
            }
        }

        if (deleted == 0)
        {
            if (!_aiBusy)
                _aiStatusLabel.Text = "No saved OCR file to delete";
            UpdateSavedCacheUiState();
            return;
        }

        _overlayBlocks.Clear();
        _lastTranslations = new List<string>();
        _savedTranslationForCurrentImage = null;
        _ocrImagePath = null;
        _lastOcrResult = null;
        _aiOutputBox.Clear();
        _pictureBox.Invalidate();
        _currentOverlayFromSavedCache = false;

        if (!_aiBusy)
            _aiStatusLabel.Text = "Deleted saved OCR";

        UpdateSavedCacheUiState();
    }

    private void DeleteSavedTranslationForCurrentImage()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!TryGetExistingOcrCachePath(imagePath, out string cachePath) ||
            !TryLoadSavedOcrEnvelope(imagePath, out var envelope) ||
            envelope == null ||
            !TryBuildSavedTranslation(envelope, out _))
        {
            if (!_aiBusy)
                _aiStatusLabel.Text = "No saved translation to delete";
            UpdateSavedCacheUiState();
            return;
        }

        try
        {
            envelope.TranslationTargetLanguage = "";
            envelope.TranslationSourceLanguage = "";
            envelope.TranslationModelId = "";
            envelope.TranslationFullText = "";
            envelope.TranslationLines = new List<string>();
            envelope.TranslationSavedUtcTicks = 0;

            File.WriteAllText(cachePath, SerializeOcrCacheEnvelopeForDisk(envelope));
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to delete saved translation from '{cachePath}': {ex.Message}");
            if (!_aiBusy)
                _aiStatusLabel.Text = "Failed to delete saved translation";
            UpdateSavedCacheUiState();
            return;
        }

        _savedTranslationForCurrentImage = null;
        _lastTranslations = new List<string>();
        SetShowSavedTranslationChecked(false, updatePreference: true);

        if (_lastOcrResult != null && string.Equals(_ocrImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
        {
            SetOverlayFromOcrResult(_lastOcrResult, null);
            _aiOutputBox.Text = RenderOcrResult(_lastOcrResult);
            _aiOutputBox.AppendText(Environment.NewLine + Environment.NewLine + "[Translation deleted from OCR_output cache]");
        }

        _pictureBox.Invalidate();
        if (!_aiBusy)
            _aiStatusLabel.Text = "Deleted saved translation";

        UpdateSavedCacheUiState();
    }

    private void OpenSavedOcrFileForCurrentImage()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!TryGetExistingOcrCachePath(imagePath, out string cachePath))
        {
            if (!_aiBusy)
                _aiStatusLabel.Text = "No saved OCR file";
            return;
        }

        string selectArg = $"/select,\"{cachePath}\"";
        string? cacheDirectory = Path.GetDirectoryName(cachePath);
        if (string.IsNullOrWhiteSpace(cacheDirectory) || !Directory.Exists(cacheDirectory))
        {
            if (!_aiBusy)
                _aiStatusLabel.Text = "Saved OCR folder not found";
            return;
        }

        try
        {
            var existingMain = Application.OpenForms
                .OfType<MainForm>()
                .LastOrDefault(f => !f.IsDisposed);
            if (existingMain != null)
            {
                if (existingMain.WindowState == FormWindowState.Minimized)
                    existingMain.WindowState = FormWindowState.Normal;
                existingMain.Show();
                existingMain.Activate();
                existingMain.BringToFront();
                existingMain.HandleExternalPath(selectArg);
                if (!_aiBusy)
                    _aiStatusLabel.Text = $"Opened and selected: {Path.GetFileName(cachePath)}";
                return;
            }
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogExecution($"Direct open/select via existing MainForm failed: {ex.Message}", false);
        }

        try
        {
            string exePath = Application.ExecutablePath;
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                var appPsi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = selectArg,
                    UseShellExecute = true
                };
                Process.Start(appPsi);
                if (!_aiBusy)
                    _aiStatusLabel.Text = $"Opened and selected: {Path.GetFileName(cachePath)}";
                return;
            }
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogExecution($"Open/select via app executable failed: {ex.Message}", false);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = cacheDirectory,
                Verb = "open",
                UseShellExecute = true
            };
            Process.Start(psi);
            if (!_aiBusy)
                _aiStatusLabel.Text = $"Opened saved OCR folder: {Path.GetFileName(cachePath)}";
            return;
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogExecution($"Open saved OCR folder via shell failed, falling back to Explorer select: {ex.Message}", false);
        }

        try
        {
            var fallbackPsi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{cachePath}\"",
                UseShellExecute = true
            };
            Process.Start(fallbackPsi);
            if (!_aiBusy)
                _aiStatusLabel.Text = $"Opened saved OCR file: {Path.GetFileName(cachePath)}";
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to open saved OCR path '{cachePath}': {ex.Message}");
            if (!_aiBusy)
                _aiStatusLabel.Text = "Failed to open saved OCR location";
        }
    }

    private void TryApplySavedOcrForCurrentImage(bool allowStatusUpdate)
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        if (!TryLoadSavedOcrEnvelope(imagePath, out var envelope) || envelope?.Result == null)
        {
            _savedTranslationForCurrentImage = null;
            if (_showSavedOcrCheck.Checked && _currentOverlayFromSavedCache)
            {
                _overlayBlocks.Clear();
                _lastTranslations = new List<string>();
                _ocrImagePath = null;
                _lastOcrResult = null;
                _aiOutputBox.Clear();
                _pictureBox.Invalidate();
                _currentOverlayFromSavedCache = false;
            }
            if (allowStatusUpdate && !_aiBusy && _showSavedOcrCheck.Checked)
                _aiStatusLabel.Text = "No saved OCR for this image";
            UpdateSavedCacheUiState();
            return;
        }

        _savedTranslationForCurrentImage = TryBuildSavedTranslation(envelope, out var savedTranslation) ? savedTranslation : null;

        if (!_showSavedOcrCheck.Checked)
        {
            _currentOverlayFromSavedCache = false;
            if (allowStatusUpdate && !_aiBusy)
            {
                _aiStatusLabel.Text = _savedTranslationForCurrentImage == null
                    ? "Saved OCR available"
                    : $"Saved OCR + translation available ({_savedTranslationForCurrentImage.TargetLanguage})";
            }
            UpdateSavedCacheUiState();
            return;
        }

        _ocrImagePath = imagePath;
        _lastOcrResult = envelope.Result;
        _lastTranslations = new List<string>();
        _currentOverlayFromSavedCache = true;
        SetOverlayFromOcrResult(_lastOcrResult, null);
        string cacheLabel = TryGetExistingOcrCachePath(imagePath, out string existingCachePath)
            ? Path.GetFileName(existingCachePath)
            : Path.GetFileName(GetOcrCachePath(imagePath));
        _aiOutputBox.Text = RenderOcrResult(_lastOcrResult);
        _aiOutputBox.AppendText(Environment.NewLine + Environment.NewLine + $"[Loaded from OCR_output cache: {cacheLabel}]");

        if (_savedTranslationForCurrentImage != null && _showSavedTranslationCheck.Checked)
        {
            _lastTranslations = _savedTranslationForCurrentImage.Translations?.ToList() ?? new List<string>();
            ApplyTranslationsToOverlay(_lastTranslations);
            _aiOutputBox.Text = RenderTranslatedResult(_lastOcrResult, _savedTranslationForCurrentImage);
            _aiOutputBox.AppendText(Environment.NewLine + Environment.NewLine + "[Loaded from OCR_output cache]");

            if (allowStatusUpdate && !_aiBusy)
                _aiStatusLabel.Text = $"Loaded OCR + translation ({_savedTranslationForCurrentImage.TargetLanguage})";
        }
        else if (allowStatusUpdate && !_aiBusy)
        {
            _aiStatusLabel.Text = _savedTranslationForCurrentImage == null
                ? "Loaded OCR from cache"
                : $"Loaded OCR from cache (saved translation: {_savedTranslationForCurrentImage.TargetLanguage})";
        }

        UpdateSavedCacheUiState();
    }

    private void ApplySavedTranslationToggleForCurrentImage()
    {
        if (_suppressSavedTranslationToggleEvent)
            return;
        if (!_showSavedOcrCheck.Checked)
        {
            UpdateSavedCacheUiState();
            return;
        }

        if (_showSavedTranslationCheck.Checked)
        {
            if (_lastOcrResult == null)
            {
                TryApplySavedOcrForCurrentImage(allowStatusUpdate: true);
                UpdateSavedCacheUiState();
                return;
            }

            if (_savedTranslationForCurrentImage == null)
            {
                string? imagePath = GetCurrentImagePath();
                if (!string.IsNullOrWhiteSpace(imagePath))
                    TryLoadSavedTranslationResult(imagePath, out _savedTranslationForCurrentImage);
            }

            if (_savedTranslationForCurrentImage == null)
            {
                _aiStatusLabel.Text = "No saved translation for this image";
                UpdateSavedCacheUiState();
                return;
            }

            _lastTranslations = _savedTranslationForCurrentImage.Translations?.ToList() ?? new List<string>();
            ApplyTranslationsToOverlay(_lastTranslations);
            _aiOutputBox.Text = RenderTranslatedResult(_lastOcrResult, _savedTranslationForCurrentImage);
            _aiOutputBox.AppendText(Environment.NewLine + Environment.NewLine + "[Loaded from OCR_output cache]");
            _currentOverlayFromSavedCache = true;
            _aiStatusLabel.Text = $"Showing saved translation ({_savedTranslationForCurrentImage.TargetLanguage})";
            UpdateSavedCacheUiState();
            return;
        }

        if (_lastOcrResult == null)
        {
            UpdateSavedCacheUiState();
            return;
        }

        _lastTranslations = new List<string>();
        SetOverlayFromOcrResult(_lastOcrResult, null);
        _aiOutputBox.Text = RenderOcrResult(_lastOcrResult);
        _aiOutputBox.AppendText(Environment.NewLine + Environment.NewLine + "[Loaded from OCR_output cache]");
        _currentOverlayFromSavedCache = true;
        _aiStatusLabel.Text = "Showing saved OCR text";
        UpdateSavedCacheUiState();
    }

    private static LlmImageTextResult CloneOcrResult(LlmImageTextResult? source)
    {
        if (source == null)
            return new LlmImageTextResult();

        return new LlmImageTextResult
        {
            FullText = source.FullText ?? "",
            DetectedLanguage = source.DetectedLanguage ?? "",
            Blocks = source.Blocks?
                .Select(b => new LlmImageTextBlock
                {
                    Text = b.Text ?? "",
                    X = b.X,
                    Y = b.Y,
                    W = b.W,
                    H = b.H,
                    FontSize = b.FontSize
                })
                .ToList() ?? new List<LlmImageTextBlock>()
        };
    }

    private static LlmTextTranslationResult? CloneTranslationResult(LlmTextTranslationResult? source)
    {
        if (source == null)
            return null;

        return new LlmTextTranslationResult
        {
            TargetLanguage = source.TargetLanguage ?? "",
            TranslatedFullText = source.TranslatedFullText ?? "",
            Translations = source.Translations?.ToList() ?? new List<string>()
        };
    }

    private LlmImageTextResult GetBestBaseOcrForImage(string imagePath)
    {
        if (TryLoadSavedOcrEnvelope(imagePath, out var savedEnvelope) && savedEnvelope?.Result != null)
            return CloneOcrResult(savedEnvelope.Result);
        if (string.Equals(_ocrImagePath, imagePath, StringComparison.OrdinalIgnoreCase) && _lastOcrResult != null)
            return CloneOcrResult(_lastOcrResult);
        return new LlmImageTextResult();
    }

    private LlmTextTranslationResult? GetBestSavedTranslationForImage(string imagePath)
    {
        if (TryLoadSavedOcrEnvelope(imagePath, out var savedEnvelope) &&
            savedEnvelope != null &&
            TryBuildSavedTranslation(savedEnvelope, out var savedTranslation))
        {
            return CloneTranslationResult(savedTranslation);
        }

        if (string.Equals(_ocrImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
            return CloneTranslationResult(_savedTranslationForCurrentImage);

        return null;
    }

    private static string ComposeFullTextFromBlocks(IEnumerable<LlmImageTextBlock> blocks)
        => string.Join(
            Environment.NewLine,
            blocks.Select(b => b.Text?.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Cast<string>());

    private List<ManualOcrSnippet> CaptureManualOcrSnippetsForQueue()
    {
        var snippets = new List<ManualOcrSnippet>(_pendingManualOcrRegions.Count);
        foreach (var region in _pendingManualOcrRegions)
        {
            using var snippet = CreateManualOcrSnippetBitmap(region.NormalizedRect);
            if (snippet == null)
                continue;

            string tempPath = Path.Combine(Path.GetTempPath(), $"speedexplorer-ocr-{Guid.NewGuid():N}.png");
            snippet.Save(tempPath, ImageFormat.Png);
            snippets.Add(new ManualOcrSnippet
            {
                NormalizedRect = UnrotateNormalizedRect(region.NormalizedRect, _rotationQuarterTurns),
                TempPath = tempPath
            });
        }

        return snippets;
    }

    private static void CleanupManualOcrSnippets(IEnumerable<ManualOcrSnippet>? snippets)
    {
        if (snippets == null)
            return;

        foreach (var snippet in snippets)
        {
            if (snippet == null || string.IsNullOrWhiteSpace(snippet.TempPath))
                continue;

            try
            {
                if (File.Exists(snippet.TempPath))
                    File.Delete(snippet.TempPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to delete manual OCR temp snippet '{snippet.TempPath}': {ex.Message}");
            }
        }
    }

    private Bitmap? CreateManualOcrSnippetBitmap(RectangleF normalizedRect)
    {
        if (_currentImage == null)
            return null;

        var pixelRect = NormalizeRectToPixels(normalizedRect, _currentImage.Size);
        if (pixelRect.Width < 1 || pixelRect.Height < 1)
            return null;

        var snippet = new Bitmap(pixelRect.Width, pixelRect.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(snippet);
        g.Clear(Color.Transparent);
        g.DrawImage(_currentImage, new Rectangle(0, 0, snippet.Width, snippet.Height), pixelRect, GraphicsUnit.Pixel);
        return snippet;
    }

    private async Task<(List<LlmImageTextBlock> Blocks, string DetectedLanguage)> ExtractManualOcrBlocksAsync(
        IReadOnlyList<ManualOcrSnippet> snippets,
        string model,
        bool useOcrReasoning,
        string sourceLanguageHint,
        string ocrHint,
        CancellationToken cancellationToken)
    {
        var blocks = new List<LlmImageTextBlock>(snippets.Count);
        string detectedLanguage = "";

        for (int i = 0; i < snippets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string text = (await _llmService.ExtractSnippetTextAsync(snippets[i].TempPath, model, cancellationToken, useReasoning: useOcrReasoning, sourceLanguageHint: sourceLanguageHint, ocrHint: ocrHint))?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text))
                continue;

            blocks.Add(new LlmImageTextBlock
            {
                Text = text,
                X = snippets[i].NormalizedRect.X,
                Y = snippets[i].NormalizedRect.Y,
                W = snippets[i].NormalizedRect.Width,
                H = snippets[i].NormalizedRect.Height,
                FontSize = 0f
            });
        }

        return (blocks, detectedLanguage);
    }

    private static LlmImageTextResult MergeManualBlocksIntoOcr(
        LlmImageTextResult baseOcr,
        IReadOnlyList<LlmImageTextBlock> manualBlocks,
        string detectedLanguage)
    {
        var merged = CloneOcrResult(baseOcr);
        merged.Blocks ??= new List<LlmImageTextBlock>();
        merged.Blocks.AddRange(manualBlocks);
        merged.FullText = ComposeFullTextFromBlocks(merged.Blocks);
        if (string.IsNullOrWhiteSpace(merged.DetectedLanguage) && !string.IsNullOrWhiteSpace(detectedLanguage))
            merged.DetectedLanguage = detectedLanguage;
        return merged;
    }

    private async Task<LlmTextTranslationResult?> BuildMergedManualTranslationAsync(
        string imagePath,
        LlmImageTextResult mergedOcr,
        LlmTextTranslationResult? existingTranslation,
        IReadOnlyList<LlmImageTextBlock> manualBlocks,
        string targetLanguage,
        string sourceLanguageHint,
        string translationContextHint,
        bool useMaximumEffortManualTranslation,
        bool useTranslationReasoning,
        string? model,
        CancellationToken cancellationToken)
    {
        var manualTexts = manualBlocks
            .Select(b => b.Text?.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Cast<string>()
            .ToList();

        var allTexts = mergedOcr.Blocks
            .Select(b => b.Text?.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Cast<string>()
            .ToList();
        if (allTexts.Count == 0 && !string.IsNullOrWhiteSpace(mergedOcr.FullText))
            allTexts.Add(mergedOcr.FullText.Trim());

        if (useMaximumEffortManualTranslation && allTexts.Count > 0)
        {
            return await _llmService.TranslateTextBlocksWithContextImageAsync(
                allTexts,
                targetLanguage,
                imagePath,
                GetTranslationSourceLanguageHint(sourceLanguageHint, mergedOcr.DetectedLanguage),
                translationContextHint,
                model,
                cancellationToken,
                useReasoning: useTranslationReasoning);
        }

        bool canAppendToSavedTranslation =
            existingTranslation != null &&
            string.Equals(NormalizeLanguageKey(existingTranslation.TargetLanguage), NormalizeLanguageKey(targetLanguage), StringComparison.Ordinal) &&
            existingTranslation.Translations != null &&
            existingTranslation.Translations.Count == Math.Max(0, mergedOcr.Blocks.Count - manualTexts.Count);

        if (canAppendToSavedTranslation && manualTexts.Count > 0)
        {
            var translatedManual = await TranslateManualBlocksAsync(manualTexts, targetLanguage, translationContextHint, model, useTranslationReasoning, cancellationToken);
            if (translatedManual == null)
                return null;

            var mergedLines = existingTranslation!.Translations!.ToList();
            mergedLines.AddRange(translatedManual);
            return new LlmTextTranslationResult
            {
                TargetLanguage = existingTranslation.TargetLanguage,
                Translations = mergedLines,
                TranslatedFullText = string.Join(Environment.NewLine, mergedLines.Where(t => !string.IsNullOrWhiteSpace(t)))
            };
        }

        var translatedAll = await TranslateManualBlocksAsync(allTexts, targetLanguage, translationContextHint, model, useTranslationReasoning, cancellationToken);
        if (translatedAll == null)
            return null;

        return new LlmTextTranslationResult
        {
            TargetLanguage = targetLanguage,
            Translations = translatedAll,
            TranslatedFullText = string.Join(Environment.NewLine, translatedAll.Where(t => !string.IsNullOrWhiteSpace(t)))
        };
    }

    private async Task<List<string>?> TranslateManualBlocksAsync(
        IReadOnlyList<string> sourceBlocks,
        string targetLanguage,
        string translationContextHint,
        string? model,
        bool useTranslationReasoning,
        CancellationToken cancellationToken)
    {
        var translations = new List<string>(sourceBlocks.Count);
        for (int i = 0; i < sourceBlocks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = sourceBlocks[i]?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(source))
            {
                translations.Add(string.Empty);
                continue;
            }

            string? translated = await _llmService.TranslateSimpleTextAsync(source, targetLanguage, model, cancellationToken, useReasoning: useTranslationReasoning, contextHint: translationContextHint);
            if (translated == null)
                return null;

            translations.Add(translated.Trim());
        }

        return translations;
    }

    private static string? GetTranslationSourceLanguageHint(string? userHint, string? detectedLanguage)
    {
        if (!string.IsNullOrWhiteSpace(userHint))
            return userHint.Trim();
        if (!string.IsNullOrWhiteSpace(detectedLanguage))
            return detectedLanguage.Trim();
        return null;
    }

    private async Task RunViewerOcrAsync(bool withTranslation)
    {
        if (IsCurrentImageActivelyProcessing())
            return;

        if (_tagCts != null)
        {
            RefreshAiStatusLabel("Wait for tagging to finish before queueing OCR or translation");
            return;
        }

        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        string targetLanguage = string.IsNullOrWhiteSpace(_targetLanguageBox.Text) ? "English" : _targetLanguageBox.Text.Trim();
        List<ManualOcrSnippet>? manualSnippets = null;

        try
        {
            string actionName = withTranslation ? "translation" : "OCR";
            SetAiBusy(HasQueuedAiWork(), $"Resolving model for queued {actionName}...");
            string? model = await EnsureVisionModelAsync();
            if (string.IsNullOrWhiteSpace(model))
            {
                SetAiBusy(HasQueuedAiWork(), "Model selection cancelled");
                return;
            }

            if (_pendingManualOcrRegions.Count > 0)
            {
                manualSnippets = CaptureManualOcrSnippetsForQueue();
                if (manualSnippets.Count == 0)
                {
                    RefreshAiStatusLabel("No text snippets were captured from the manual OCR boxes");
                    return;
                }

                ClearPendingManualOcrRegions(updateStatus: false);
            }

            var job = new ImageAiJob
            {
                ImagePath = imagePath,
                WithTranslation = withTranslation,
                UseMaximumEffortManualTranslation = withTranslation && _manualMaxEffortCheck.Checked,
                UseOcrReasoning = _ocrReasoningCheck.Checked,
                UseTranslationReasoning = _translationReasoningCheck.Checked,
                TargetLanguage = targetLanguage,
                SourceLanguageHint = _sourceLanguageHintBox.Text.Trim(),
                OcrHint = _ocrHintBox.Text.Trim(),
                TranslationContextHint = withTranslation ? _translationContextHintBox.Text.Trim() : "",
                ModelId = model,
                ManualSnippets = manualSnippets ?? new List<ManualOcrSnippet>()
            };

            EnqueueAiJob(job);
        }
        catch (Exception ex)
        {
            CleanupManualOcrSnippets(manualSnippets);
            SetAiBusy(HasQueuedAiWork(), $"AI queue error: {ex.Message}");
            LlmDebugLogger.LogError($"Failed to enqueue image viewer OCR/translate job: {ex}");
        }
    }

    private void EnqueueAiJob(ImageAiJob job)
    {
        _queuedAiJobs.Add(job);
        IncrementQueuedAiJobsForImage(job.ImagePath);
        RegisterQueuedManualRegions(job);
        SetAiBusy(true, BuildQueuedJobStatus(job));
        UpdateSavedCacheUiState();
        _pictureBox.Invalidate();
        _ = ProcessQueuedAiJobsAsync();
    }

    private ImageAiJob TakeNextQueuedAiJob(string? preferredImagePath)
    {
        int index = 0;
        if (!string.IsNullOrWhiteSpace(preferredImagePath))
        {
            int preferredIndex = _queuedAiJobs.FindIndex(job =>
                string.Equals(job.ImagePath, preferredImagePath, StringComparison.OrdinalIgnoreCase));
            if (preferredIndex >= 0)
                index = preferredIndex;
        }

        var job = _queuedAiJobs[index];
        _queuedAiJobs.RemoveAt(index);
        return job;
    }

    private string BuildQueuedJobStatus(ImageAiJob job)
    {
        string action = job.WithTranslation ? "translation" : "OCR";
        if (job.WithTranslation && job.UseMaximumEffortManualTranslation)
        {
            if (job.ManualSnippets.Count > 0)
                return $"Queued max-effort manual {action} for {Path.GetFileName(job.ImagePath)}";
            return $"Queued max-effort {action} for {Path.GetFileName(job.ImagePath)}";
        }

        if (job.ManualSnippets.Count > 0)
        {
            return $"Queued manual {action} for {Path.GetFileName(job.ImagePath)}";
        }
        return $"Queued {action} for {Path.GetFileName(job.ImagePath)}";
    }

    private async Task ProcessQueuedAiJobsAsync()
    {
        if (_activeAiJob != null || _aiCts != null)
            return;

        _aiCts = new CancellationTokenSource();
        string? preferredImagePath = null;
        try
        {
            while (_queuedAiJobs.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(preferredImagePath) &&
                    !_queuedAiJobs.Any(job => string.Equals(job.ImagePath, preferredImagePath, StringComparison.OrdinalIgnoreCase)))
                {
                    preferredImagePath = null;
                }

                var job = TakeNextQueuedAiJob(preferredImagePath);
                DecrementQueuedAiJobsForImage(job.ImagePath);
                _activeAiJob = job;
                preferredImagePath = job.ImagePath;

                string processingStatus = job.ManualSnippets.Count > 0
                    ? (job.WithTranslation
                        ? (job.UseMaximumEffortManualTranslation ? "Processing queued max-effort manual translation..." : "Processing queued manual translation...")
                        : "Processing queued manual OCR...")
                    : (job.WithTranslation
                        ? (job.UseMaximumEffortManualTranslation ? "Processing queued max-effort translation..." : "Processing queued translation...")
                        : "Processing queued OCR...");
                SetAiBusy(true, processingStatus);

                ImageAiJobResult result;
                try
                {
                    result = await ExecuteAiJobAsync(
                        job,
                        _aiCts.Token,
                        progressResult =>
                        {
                            if (progressResult.Ocr != null && job.ManualSnippets.Count > 0)
                            {
                                UnregisterQueuedManualRegions(job);
                                _pictureBox.Invalidate();
                            }

                            bool appliedProgress = ApplyAiJobResultIfCurrent(job, progressResult);
                            RefreshAiStatusLabel(appliedProgress ? progressResult.StatusText : null);
                        });
                }
                catch (OperationCanceledException)
                {
                    bool cancelOnlyThisJob = _cancelActiveAiJobOnly;
                    _cancelActiveAiJobOnly = false;
                    UnregisterQueuedManualRegions(job);
                    CleanupManualOcrSnippets(job.ManualSnippets);
                    _activeAiJob = null;
                    _pictureBox.Invalidate();

                    if (cancelOnlyThisJob)
                    {
                        RefreshAiStatusLabel("Cancelled current AI job");
                        _aiCts?.Dispose();
                        _aiCts = _queuedAiJobs.Count > 0 ? new CancellationTokenSource() : null;
                        if (_aiCts == null)
                            break;
                        continue;
                    }

                    RefreshAiStatusLabel("Operation aborted");
                    break;
                }
                catch (Exception ex)
                {
                    UnregisterQueuedManualRegions(job);
                    CleanupManualOcrSnippets(job.ManualSnippets);
                    LlmDebugLogger.LogError($"Queued image viewer AI job failed: {ex}");
                    result = new ImageAiJobResult
                    {
                        ImagePath = job.ImagePath,
                        StatusText = $"AI error: {ex.Message}",
                        ErrorText = ex.Message
                    };
                }

                UnregisterQueuedManualRegions(job);
                CleanupManualOcrSnippets(job.ManualSnippets);
                bool appliedToCurrent = ApplyAiJobResultIfCurrent(job, result);
                RefreshAiStatusLabel(appliedToCurrent ? result.StatusText : null);
                _activeAiJob = null;
                UpdateCancelCurrentJobButton();
                _pictureBox.Invalidate();
            }
        }
        finally
        {
            _activeAiJob = null;
            _cancelActiveAiJobOnly = false;
            _aiCts?.Dispose();
            _aiCts = null;
            string finalStatus = _aiStatusLabel.Text;
            SetAiBusy(false, finalStatus);
        }
    }

    private async Task<ImageAiJobResult> ExecuteAiJobAsync(
        ImageAiJob job,
        CancellationToken cancellationToken,
        Action<ImageAiJobResult>? progressCallback = null)
    {
        string imagePath = job.ImagePath;
        string? model = job.ModelId;

        if (job.ManualSnippets.Count > 0)
        {
            var baseOcr = GetBestBaseOcrForImage(imagePath);
            var existingTranslation = GetBestSavedTranslationForImage(imagePath);
            var (manualBlocks, detectedLanguage) = await ExtractManualOcrBlocksAsync(job.ManualSnippets, model ?? "", job.UseOcrReasoning, job.SourceLanguageHint, job.OcrHint, cancellationToken);
            if (manualBlocks.Count == 0)
            {
                return new ImageAiJobResult
                {
                    ImagePath = imagePath,
                    StatusText = "Manual OCR found no text",
                    ErrorText = "No text was found inside the selected manual OCR boxes."
                };
            }

            var mergedOcr = MergeManualBlocksIntoOcr(baseOcr, manualBlocks, detectedLanguage);
            SaveOcrResultToCache(imagePath, model, mergedOcr);
            if (job.WithTranslation)
            {
                progressCallback?.Invoke(new ImageAiJobResult
                {
                    ImagePath = imagePath,
                    Ocr = mergedOcr,
                    StatusText = $"Added {manualBlocks.Count} manual OCR box(es); translating..."
                });
            }

            if (!job.WithTranslation)
            {
                return new ImageAiJobResult
                {
                    ImagePath = imagePath,
                    Ocr = mergedOcr,
                    StatusText = $"Added {manualBlocks.Count} manual OCR box(es)"
                };
            }

            var mergedTranslation = await BuildMergedManualTranslationAsync(
                imagePath,
                mergedOcr,
                existingTranslation,
                manualBlocks,
                job.TargetLanguage,
                job.SourceLanguageHint,
                job.TranslationContextHint,
                job.UseMaximumEffortManualTranslation,
                job.UseTranslationReasoning,
                model,
                cancellationToken);
            if (mergedTranslation == null)
            {
                return new ImageAiJobResult
                {
                    ImagePath = imagePath,
                    Ocr = mergedOcr,
                    StatusText = "Translation failed",
                    ErrorText = "Translation failed"
                };
            }

            SaveTranslationToCache(imagePath, model, mergedOcr, mergedTranslation);
            return new ImageAiJobResult
            {
                ImagePath = imagePath,
                Ocr = mergedOcr,
                Translation = mergedTranslation,
                ShowSavedTranslation = true,
                StatusText = $"Added and translated {manualBlocks.Count} manual OCR box(es)"
            };
        }

        LlmImageTextResult? ocr = null;
        bool usingSavedOcr = false;

        if (job.WithTranslation && TryLoadSavedOcrEnvelope(imagePath, out var savedEnvelope) && savedEnvelope?.Result != null)
        {
            ocr = CloneOcrResult(savedEnvelope.Result);
            if (!job.UseMaximumEffortManualTranslation &&
                TryBuildSavedTranslation(savedEnvelope, out var savedTranslation) &&
                savedTranslation != null &&
                string.Equals(
                    NormalizeLanguageKey(savedTranslation.TargetLanguage),
                    NormalizeLanguageKey(job.TargetLanguage),
                    StringComparison.Ordinal))
            {
                return new ImageAiJobResult
                {
                    ImagePath = imagePath,
                    Ocr = ocr,
                    Translation = savedTranslation,
                    FromSavedCache = true,
                    ShowSavedTranslation = true,
                    StatusText = $"Loaded saved translation ({savedTranslation.TargetLanguage})"
                };
            }

            usingSavedOcr = true;
        }

        if (!usingSavedOcr)
        {
            ocr = await _llmService.ExtractImageTextAsync(imagePath, model, cancellationToken, useReasoning: job.UseOcrReasoning, sourceLanguageHint: job.SourceLanguageHint, ocrHint: job.OcrHint);
            if (ocr == null)
            {
                return new ImageAiJobResult
                {
                    ImagePath = imagePath,
                    StatusText = "OCR failed",
                    ErrorText = "Failed to extract text from the image."
                };
            }

            SaveOcrResultToCache(imagePath, model, ocr);
            if (job.WithTranslation)
            {
                progressCallback?.Invoke(new ImageAiJobResult
                {
                    ImagePath = imagePath,
                    Ocr = ocr,
                    StatusText = $"OCR regenerated ({ocr.Blocks.Count} blocks); translating..."
                });
            }

            if (!job.WithTranslation)
            {
                return new ImageAiJobResult
                {
                    ImagePath = imagePath,
                    Ocr = ocr,
                    StatusText = $"OCR regenerated ({ocr.Blocks.Count} blocks)"
                };
            }
        }

        if (ocr == null)
        {
            return new ImageAiJobResult
            {
                ImagePath = imagePath,
                StatusText = "OCR failed",
                ErrorText = "OCR failed"
            };
        }

        var sourceBlocks = ocr.Blocks.Select(b => b.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (sourceBlocks.Count == 0 && !string.IsNullOrWhiteSpace(ocr.FullText))
            sourceBlocks.Add(ocr.FullText);

        var translation = job.UseMaximumEffortManualTranslation
            ? await _llmService.TranslateTextBlocksWithContextImageAsync(
                sourceBlocks,
                job.TargetLanguage,
                imagePath,
                GetTranslationSourceLanguageHint(job.SourceLanguageHint, ocr.DetectedLanguage),
                job.TranslationContextHint,
                model,
                cancellationToken,
                useReasoning: job.UseTranslationReasoning)
            : await _llmService.TranslateTextBlocksAsync(
                sourceBlocks,
                job.TargetLanguage,
                GetTranslationSourceLanguageHint(job.SourceLanguageHint, ocr.DetectedLanguage),
                job.TranslationContextHint,
                model,
                cancellationToken,
                useReasoning: job.UseTranslationReasoning);
        if (translation == null)
        {
            return new ImageAiJobResult
            {
                ImagePath = imagePath,
                Ocr = ocr,
                StatusText = "Translation failed",
                ErrorText = "Translation failed"
            };
        }

        SaveTranslationToCache(imagePath, model, ocr, translation);
        return new ImageAiJobResult
        {
            ImagePath = imagePath,
            Ocr = ocr,
            Translation = translation,
            ShowSavedTranslation = true,
            StatusText = $"Translated to {translation.TargetLanguage}"
        };
    }

    private bool ApplyAiJobResultIfCurrent(ImageAiJob job, ImageAiJobResult result)
    {
        if (!string.Equals(GetCurrentImagePath(), result.ImagePath, StringComparison.OrdinalIgnoreCase))
        {
            UpdateSavedCacheUiState();
            return false;
        }

        CancelOverlayDrag(invalidate: false);

        if (!string.IsNullOrWhiteSpace(result.ErrorText))
        {
            if (!string.IsNullOrWhiteSpace(result.ErrorText))
                _aiOutputBox.Text = result.ErrorText;
            UpdateSavedCacheUiState();
            return true;
        }

        if (result.Ocr == null)
        {
            UpdateSavedCacheUiState();
            return true;
        }

        ApplyLoadedOcrToViewer(result.ImagePath, result.Ocr, result.FromSavedCache);
        _savedTranslationForCurrentImage = result.Translation;
        _lastTranslations = result.Translation?.Translations?.ToList() ?? new List<string>();

        if (result.Translation != null)
        {
            ApplyTranslationsToOverlay(_lastTranslations);
            _aiOutputBox.Text = RenderTranslatedResult(result.Ocr, result.Translation);
            if (result.FromSavedCache)
                _aiOutputBox.AppendText(Environment.NewLine + Environment.NewLine + "[Loaded from OCR_output cache]");
            SetShowSavedTranslationChecked(result.ShowSavedTranslation, updatePreference: result.ShowSavedTranslation);
            _currentOverlayFromSavedCache = result.FromSavedCache;
        }
        else
        {
            SetShowSavedTranslationChecked(false);
            _currentOverlayFromSavedCache = result.FromSavedCache;
        }

        UpdateSavedCacheUiState();
        return true;
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
            _activeTagImagePath = imagePath;
            SetAiBusy(true, "Resolving model...");
            string? model = await EnsureVisionModelAsync();
            if (string.IsNullOrWhiteSpace(model))
            {
                SetAiBusy(false, "Model selection cancelled");
                return;
            }

            SetAiBusy(true, "Generating tags...");
            _tagCts = new CancellationTokenSource();
            var tags = await _llmService.GetImageTagsAsync(
                "Analyze this image and return concise descriptive tags only. Prefer 8 to 20 tags.",
                imagePath,
                model,
                _tagCts.Token);

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
        catch (OperationCanceledException)
        {
            SetAiBusy(false, "Operation aborted");
        }
        catch (Exception ex)
        {
            SetAiBusy(false, $"Tagging error: {ex.Message}");
            LlmDebugLogger.LogError($"Image viewer tagging failed: {ex}");
        }
        finally
        {
            _tagCts?.Dispose();
            _tagCts = null;
            _activeTagImagePath = null;
            UpdateAiActionControlsState();
            UpdateManualOcrUiState();
            UpdateSavedCacheUiState();
        }
    }

    private void AbortAi()
    {
        try
        {
            _cancelActiveAiJobOnly = false;
            if (_activeAiJob != null)
                RestoreManualRegionsFromAbortedJob(_activeAiJob);

            _aiCts?.Cancel();
            _tagCts?.Cancel();

            while (_queuedAiJobs.Count > 0)
            {
                var job = _queuedAiJobs[0];
                _queuedAiJobs.RemoveAt(0);
                RestoreManualRegionsFromAbortedJob(job);
                DecrementQueuedAiJobsForImage(job.ImagePath);
                UnregisterQueuedManualRegions(job);
                CleanupManualOcrSnippets(job.ManualSnippets);
            }

            UpdateManualOcrUiState();
            UpdateCancelCurrentJobButton();
            RefreshAiStatusLabel("Aborting...");
            _pictureBox.Invalidate();
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to abort AI: {ex.Message}");
        }
    }

    private void CancelAiJobForCurrentImage()
    {
        try
        {
            string? imagePath = GetCurrentImagePath();
            if (string.IsNullOrWhiteSpace(imagePath))
                return;

            if (_activeAiJob != null &&
                string.Equals(_activeAiJob.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
            {
                RestoreManualRegionsFromAbortedJob(_activeAiJob);
                _cancelActiveAiJobOnly = true;
                _aiCts?.Cancel();
                RefreshAiStatusLabel("Cancelling current AI job...");
                UpdateCancelCurrentJobButton();
                _pictureBox.Invalidate();
                return;
            }

            int queuedIndex = _queuedAiJobs.FindIndex(job =>
                string.Equals(job.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase));
            if (queuedIndex < 0)
                return;

            var queuedJob = _queuedAiJobs[queuedIndex];
            _queuedAiJobs.RemoveAt(queuedIndex);
            RestoreManualRegionsFromAbortedJob(queuedJob);
            DecrementQueuedAiJobsForImage(queuedJob.ImagePath);
            UnregisterQueuedManualRegions(queuedJob);
            CleanupManualOcrSnippets(queuedJob.ManualSnippets);

            UpdateManualOcrUiState();
            UpdateCancelCurrentJobButton();
            SetAiBusy(HasQueuedAiWork(), "Cancelled queued AI job");
            _pictureBox.Invalidate();
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to cancel current image AI job: {ex.Message}");
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

    private static RectangleF RotateNormalizedRectClockwise(RectangleF rect)
        => ClampNormalizedRect(1f - (rect.Y + rect.Height), rect.X, rect.Height, rect.Width);

    private void ApplyCurrentRotationToOverlayBlocks()
    {
        if (_rotationQuarterTurns == 0 || _overlayBlocks.Count == 0)
            return;

        for (int turn = 0; turn < _rotationQuarterTurns; turn++)
        {
            for (int i = 0; i < _overlayBlocks.Count; i++)
                _overlayBlocks[i].NormalizedRect = RotateNormalizedRectClockwise(_overlayBlocks[i].NormalizedRect);
        }
    }

    private void RotateImageClockwise()
    {
        if (_currentImage == null)
            return;

        if (_currentAnimation != null)
        {
            _currentAnimation.RotateClockwise();
            _currentImage = _currentAnimation.GetFrame(_animationFrameIndex);
        }
        else
        {
            _currentImage.RotateFlip(RotateFlipType.Rotate90FlipNone);
        }

        _rotationQuarterTurns = (_rotationQuarterTurns + 1) % 4;
        for (int i = 0; i < _overlayBlocks.Count; i++)
            _overlayBlocks[i].NormalizedRect = RotateNormalizedRectClockwise(_overlayBlocks[i].NormalizedRect);

        if (_autoFitEnabled)
        {
            if (_autoFitBySmallerDimension)
                FitToWindowBySmallerDimension(allowUpscale: false);
            else
                FitToWindow(allowUpscale: false);
        }
        else
        {
            _pictureBox.Invalidate();
        }

        if (!_aiBusy)
            _aiStatusLabel.Text = "Rotated clockwise";
    }

    private void SetOverlayFromOcrResult(LlmImageTextResult ocr, IReadOnlyList<string>? translatedLines)
    {
        _overlayBlocks.Clear();

        bool hasPixelCoordinates = ocr.Blocks.Any(b => b.X > 10.0f || b.Y > 10.0f || b.W > 10.0f || b.H > 10.0f);
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
                ? NormalizeOverlayDisplayText(StripOrderedPrefix(translatedLines[i]))
                : NormalizeOverlayDisplayText(block.Text);

            _overlayBlocks.Add(new OverlayTextBlock
            {
                SourceIndex = i,
                SourceText = block.Text,
                DisplayText = translated,
                NormalizedRect = rect,
                NormalizedFontSize = normalizedFontSize
            });
        }

        var reduced = ReduceOverlayBlocksConservatively(_overlayBlocks);
        if (reduced.Count != _overlayBlocks.Count)
        {
            _overlayBlocks.Clear();
            _overlayBlocks.AddRange(reduced);
        }

        ApplyCurrentRotationToOverlayBlocks();

        _pictureBox.Invalidate();
    }

    private void ApplyTranslationsToOverlay(IReadOnlyList<string> translatedLines)
    {
        if (_overlayBlocks.Count == 0)
            return;

        for (int i = 0; i < _overlayBlocks.Count; i++)
        {
            int sourceIndex = _overlayBlocks[i].SourceIndex;
            if (sourceIndex >= 0 && sourceIndex < translatedLines.Count && !string.IsNullOrWhiteSpace(translatedLines[sourceIndex]))
                _overlayBlocks[i].DisplayText = NormalizeOverlayDisplayText(StripOrderedPrefix(translatedLines[sourceIndex]));
        }

        ApplyCachedOverlayOverridesForCurrentImage(invalidate: false);
        _pictureBox.Invalidate();
    }

    private void ApplyCachedOverlayOverridesForCurrentImage(bool invalidate = true)
    {
        string? imagePath = GetCurrentImagePath();
        _currentImageOverlayDefaults = null;
        if (string.IsNullOrWhiteSpace(imagePath) ||
            !TryLoadSavedOcrEnvelope(imagePath, out var envelope) ||
            envelope == null)
        {
            return;
        }

        _currentImageOverlayDefaults = envelope.OverlayDefaults?.Clone();
        if (envelope.OverlayOverrides == null || envelope.OverlayOverrides.Count == 0)
        {
            if (invalidate)
                _pictureBox.Invalidate();
            return;
        }

        bool showingTranslation = _showSavedTranslationCheck.Checked && _savedTranslationForCurrentImage != null;
        foreach (var block in _overlayBlocks)
        {
            var ov = envelope.OverlayOverrides.LastOrDefault(o => o.SourceIndex == block.SourceIndex);
            if (ov == null)
                continue;

            block.HasUserOverride = true;
            if (ov.W > 0f && ov.H > 0f)
                block.NormalizedRect = ClampNormalizedRect(ov.X, ov.Y, ov.W, ov.H);
            if (ov.FontSize > 0f)
                block.NormalizedFontSize = Math.Clamp(ov.FontSize, 0f, 0.5f);
            if (ov.TextColorArgb != null)
                block.TextColorArgb = ov.TextColorArgb;
            if (ov.TextOutlineColorArgb != null)
                block.TextOutlineColorArgb = ov.TextOutlineColorArgb;
            if (ov.TextAlignment != null)
                block.TextAlignment = ov.TextAlignment;
            if (ov.TextVerticalAlignment != null)
                block.TextVerticalAlignment = ov.TextVerticalAlignment;
            if (ov.TextOutlineVisible != null)
                block.TextOutlineVisible = ov.TextOutlineVisible;
            if (ov.BoxFillColorArgb != null)
                block.BoxFillColorArgb = ov.BoxFillColorArgb;
            if (ov.BoxBorderColorArgb != null)
                block.BoxBorderColorArgb = ov.BoxBorderColorArgb;
            if (ov.BoxFillVisible != null)
                block.BoxFillVisible = ov.BoxFillVisible;
            if (ov.BoxBorderVisible != null)
                block.BoxBorderVisible = ov.BoxBorderVisible;
            if (!string.IsNullOrWhiteSpace(ov.Text))
                block.SourceText = ov.Text!;

            string? displayOverride = showingTranslation ? ov.TranslationText : ov.Text;
            if (!string.IsNullOrWhiteSpace(displayOverride))
                block.DisplayText = NormalizeEditedOverlayDisplayText(displayOverride!);
        }

        if (invalidate)
            _pictureBox.Invalidate();
    }

    private OverlayStyleDefaults GetGlobalOverlayDefaults()
        => new()
        {
            TextColorArgb = _settings.ImageViewerOverlayDefaultTextColorArgb,
            TextOutlineColorArgb = _settings.ImageViewerOverlayDefaultTextOutlineColorArgb,
            TextAlignment = ToStringAlignment(_settings.ImageViewerOverlayDefaultTextAlignment),
            TextVerticalAlignment = ToStringAlignment(_settings.ImageViewerOverlayDefaultTextVerticalAlignment),
            TextOutlineVisible = _settings.ImageViewerOverlayDefaultTextOutlineVisible,
            BoxFillColorArgb = _settings.ImageViewerOverlayDefaultBoxFillColorArgb,
            BoxFillVisible = _settings.ImageViewerOverlayDefaultBoxFillVisible,
            BoxBorderColorArgb = _settings.ImageViewerOverlayDefaultBoxBorderColorArgb,
            BoxBorderVisible = _settings.ImageViewerOverlayDefaultBoxBorderVisible
        };

    private OverlayStyleDefaults GetEffectiveOverlayStyle(OverlayTextBlock block)
    {
        OverlayStyleDefaults global = GetGlobalOverlayDefaults();
        OverlayStyleDefaults? image = _currentImageOverlayDefaults;
        return new OverlayStyleDefaults
        {
            TextColorArgb = block.TextColorArgb ?? image?.TextColorArgb ?? global.TextColorArgb,
            TextOutlineColorArgb = block.TextOutlineColorArgb ?? image?.TextOutlineColorArgb ?? global.TextOutlineColorArgb,
            TextAlignment = block.TextAlignment ?? image?.TextAlignment ?? global.TextAlignment,
            TextVerticalAlignment = block.TextVerticalAlignment ?? image?.TextVerticalAlignment ?? global.TextVerticalAlignment,
            TextOutlineVisible = block.TextOutlineVisible ?? image?.TextOutlineVisible ?? global.TextOutlineVisible,
            BoxFillColorArgb = block.BoxFillColorArgb ?? image?.BoxFillColorArgb ?? global.BoxFillColorArgb,
            BoxFillVisible = block.BoxFillVisible ?? image?.BoxFillVisible ?? global.BoxFillVisible,
            BoxBorderColorArgb = block.BoxBorderColorArgb ?? image?.BoxBorderColorArgb ?? global.BoxBorderColorArgb,
            BoxBorderVisible = block.BoxBorderVisible ?? image?.BoxBorderVisible ?? global.BoxBorderVisible
        };
    }

    private static StringAlignment? ToStringAlignment(int? value)
        => value switch
        {
            0 => StringAlignment.Near,
            1 => StringAlignment.Center,
            2 => StringAlignment.Far,
            _ => null
        };

    private static int? FromStringAlignment(StringAlignment? value)
        => value switch
        {
            StringAlignment.Near => 0,
            StringAlignment.Center => 1,
            StringAlignment.Far => 2,
            _ => null
        };

    private static List<OverlayTextBlock> ReduceOverlayBlocksConservatively(List<OverlayTextBlock> input)
    {
        const int maxBlocks = 1000;
        if (input.Count <= 1)
            return input.ToList();

        var output = new List<OverlayTextBlock>(Math.Min(input.Count, maxBlocks));
        for (int i = 0; i < input.Count; i++)
        {
            var candidate = input[i];
            if (string.IsNullOrWhiteSpace(candidate.DisplayText))
                continue;

            float area = Math.Max(0f, candidate.NormalizedRect.Width * candidate.NormalizedRect.Height);
            if (area < 0.000001f)
                continue;

            string candidateNorm = NormalizeOverlayText(string.IsNullOrWhiteSpace(candidate.SourceText) ? candidate.DisplayText : candidate.SourceText);
            bool duplicate = false;
            int start = Math.Max(0, output.Count - 120);
            for (int j = output.Count - 1; j >= start; j--)
            {
                var prior = output[j];
                float overlap = ComputeRectOverlapRatio(candidate.NormalizedRect, prior.NormalizedRect);
                if (overlap < 0.50f)
                    continue;

                string priorNorm = NormalizeOverlayText(string.IsNullOrWhiteSpace(prior.SourceText) ? prior.DisplayText : prior.SourceText);
                bool sameText = candidateNorm.Length > 0 && candidateNorm == priorNorm;
                float candidateArea = Math.Max(0.0000001f, candidate.NormalizedRect.Width * candidate.NormalizedRect.Height);
                float priorArea = Math.Max(0.0000001f, prior.NormalizedRect.Width * prior.NormalizedRect.Height);
                float areaRatio = Math.Min(candidateArea, priorArea) / Math.Max(candidateArea, priorArea);

                if ((sameText && overlap >= 0.55f) || (sameText && overlap >= 0.80f) || (overlap >= 0.985f && areaRatio >= 0.92f))
                {
                    duplicate = true;
                    break;
                }
            }

            if (duplicate)
                continue;

            output.Add(candidate);
            if (output.Count >= maxBlocks)
                break;
        }

        return output;
    }

    private static string NormalizeOverlayText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static float ComputeRectOverlapRatio(RectangleF a, RectangleF b)
    {
        float overlapW = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        if (overlapW <= 0f)
            return 0f;

        float overlapH = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        if (overlapH <= 0f)
            return 0f;

        float overlapArea = overlapW * overlapH;
        float minArea = Math.Max(0.0000001f, Math.Min(a.Width * a.Height, b.Width * b.Height));
        return overlapArea / minArea;
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
                if (marker == ':' && i + 1 < trimmed.Length && !char.IsWhiteSpace(trimmed[i + 1]))
                    return trimmed;

                i++;
                while (i < trimmed.Length && char.IsWhiteSpace(trimmed[i]))
                    i++;
                if (i < trimmed.Length)
                    return trimmed.Substring(i);
                
                // If stripping the prefix leaves nothing, return the original text (e.g. for "1.")
                return trimmed;
            }
        }

        return trimmed;
    }

    private static string NormalizeOverlayDisplayText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string normalized = DecodeEscapedLineBreaks(text)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Trim();
        if (normalized.IndexOf('\n') < 0)
            return normalized;

        var parts = normalized
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (parts.Count <= 1)
            return parts.Count == 1 ? parts[0] : "";

        bool likelyVertical = IsLikelyVerticalText(parts);
        var sb = new StringBuilder(parts[0]);
        for (int i = 1; i < parts.Count; i++)
        {
            string next = parts[i];
            char prevLast = GetLastNonWhitespace(sb);
            char nextFirst = GetFirstNonWhitespace(next);

            if (prevLast == '-' && char.IsLetterOrDigit(nextFirst))
            {
                if (sb.Length > 0)
                    sb.Length--;
                sb.Append(next);
                continue;
            }

            if (likelyVertical || ShouldJoinWithoutSpace(prevLast, nextFirst))
            {
                sb.Append(next);
            }
            else
            {
                sb.Append(' ');
                sb.Append(next);
            }
        }

        return sb.ToString().Trim();
    }

    private static string NormalizeEditedOverlayDisplayText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        return DecodeEscapedLineBreaks(text)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static string DecodeEscapedLineBreaks(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return text
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\n", StringComparison.Ordinal);
    }

    private static bool IsLikelyVerticalText(List<string> lines)
    {
        if (lines.Count < 3)
            return false;

        int shortLines = 0;
        int cjkLines = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (line.Length <= 2)
                shortLines++;
            if (line.Any(IsCjkChar))
                cjkLines++;
        }

        return shortLines >= (int)Math.Ceiling(lines.Count * 0.70f) || cjkLines >= (int)Math.Ceiling(lines.Count * 0.70f);
    }

    private static bool ShouldJoinWithoutSpace(char left, char right)
    {
        if (left == '\0' || right == '\0')
            return false;

        if (IsCjkChar(left) || IsCjkChar(right))
            return true;

        if ("([{«“\"'".IndexOf(left) >= 0)
            return true;
        if (")]},.!?:;»”\"'".IndexOf(right) >= 0)
            return true;

        return false;
    }

    private static char GetFirstNonWhitespace(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
                return text[i];
        }
        return '\0';
    }

    private static char GetLastNonWhitespace(StringBuilder text)
    {
        for (int i = text.Length - 1; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(text[i]))
                return text[i];
        }
        return '\0';
    }

    private static bool IsCjkChar(char ch)
    {
        return ch is >= '\u3040' and <= '\u30FF'   // Hiragana + Katakana
            or >= '\u3400' and <= '\u4DBF'         // CJK Extension A
            or >= '\u4E00' and <= '\u9FFF'         // CJK Unified Ideographs
            or >= '\uF900' and <= '\uFAFF'         // CJK Compatibility Ideographs
            or >= '\uAC00' and <= '\uD7AF';        // Hangul syllables
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

        CancelOverlayDrag(invalidate: false);
        var path = _imagePaths[_currentIndex];
        _rotationQuarterTurns = 0;
        if (!string.Equals(_ocrImagePath, path, StringComparison.OrdinalIgnoreCase))
        {
            _ocrImagePath = null;
            _lastOcrResult = null;
            _savedTranslationForCurrentImage = null;
            _lastTranslations = new List<string>();
            _currentImageOverlayDefaults = null;
            _overlayBlocks.Clear();
            _pendingManualOcrRegions.Clear();
            _manualOcrDrawMode = false;
            _isDrawingManualOcrRegion = false;
            _aiOutputBox.Clear();
            _currentOverlayFromSavedCache = false;
            RestorePendingManualRegionsForCurrentImage();
            RefreshAiStatusLabel();
        }
        
        ClearAnimationState();
        try
        {
            bool isAnimated = ImageSharpViewerService.IsAnimatedImage(path);
            if (isAnimated)
            {
                _currentAnimation = ImageSharpViewerService.LoadAnimation(path);
                _animationFrameIndex = 0;
                _currentImage = _currentAnimation.GetFrame(_animationFrameIndex);
                StartAnimationIfNeeded();
            }
            else
            {
                _currentImage = ImageSharpViewerService.LoadBitmap(path);
            }
            
            _fileNameLabel.Text = Path.GetFileName(path);
            _indexLabel.Text = $"{_currentIndex + 1} / {_imagePaths.Count}";
            _titleLabel.Text = $"Speed Explorer - {Path.GetFileName(path)}";

            UpdateTags(path);
            EnsureImageFolderWatcher(path);
            FitToWindow(allowUpscale: false);
            TryApplySavedOcrForCurrentImage(allowStatusUpdate: true);
            UpdateSavedCacheUiState();
            UpdateManualOcrUiState();
            UpdateCancelCurrentJobButton();
            UpdateAiActionControlsState();
            RefreshAiStatusLabel();
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
        UpdateManualOcrUiState();
        UpdateCancelCurrentJobButton();
        UpdateAiActionControlsState();
    }

    private void EnsureImageFolderWatcher(string imagePath)
    {
        string? folder = Path.GetDirectoryName(imagePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        folder = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(_watchedImageFolder, folder, StringComparison.OrdinalIgnoreCase))
            return;

        _imageFolderWatcher?.Dispose();
        _imageFolderWatcher = null;
        _watchedImageFolder = folder;

        try
        {
            _imageFolderWatcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _imageFolderWatcher.Created += ImageFolderWatcher_FileChanged;
            _imageFolderWatcher.Renamed += ImageFolderWatcher_FileChanged;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to watch image viewer folder: {ex.Message}");
        }
    }

    private void ImageFolderWatcher_FileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            BeginInvoke(new Action(() =>
            {
                _imageFolderRefreshTimer.Stop();
                _imageFolderRefreshTimer.Start();
            }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to schedule image viewer folder refresh: {ex.Message}");
        }
    }

    private void ImageFolderRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _imageFolderRefreshTimer.Stop();
        AddNewImagesFromWatchedFolder();
    }

    private void AddNewImagesFromWatchedFolder()
    {
        if (string.IsNullOrWhiteSpace(_watchedImageFolder) || !Directory.Exists(_watchedImageFolder))
            return;

        var seen = new HashSet<string>(_imagePaths, StringComparer.OrdinalIgnoreCase);
        var added = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(_watchedImageFolder))
            {
                string fullPath;
                try { fullPath = Path.GetFullPath(file); }
                catch { continue; }

                if (!seen.Add(fullPath))
                    continue;
                if (!FileSystemService.IsImageFile(fullPath))
                    continue;

                added.Add(fullPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to add new image viewer files: {ex.Message}");
            return;
        }

        if (added.Count == 0)
            return;

        if (_sortOptions == null)
        {
            int insertIndex = FindWatchedFolderAppendIndex();
            _imagePaths.InsertRange(insertIndex, added);
        }
        else
        {
            added.Sort((a, b) => CompareImagePathsForSort(a, b, _sortOptions));
            foreach (var imagePath in added)
            {
                int insertIndex = FindWatchedFolderSortedInsertIndex(imagePath, _sortOptions);
                _imagePaths.Insert(insertIndex, imagePath);
            }
        }

        string? currentPath = GetCurrentImagePath();
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            int newIndex = _imagePaths.FindIndex(path => string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase));
            if (newIndex >= 0)
                _currentIndex = newIndex;
        }

        _indexLabel.Text = $"{_currentIndex + 1} / {_imagePaths.Count}";
    }

    private int FindWatchedFolderAppendIndex()
    {
        if (string.IsNullOrWhiteSpace(_watchedImageFolder))
            return _imagePaths.Count;

        int lastFolderImageIndex = -1;
        for (int i = 0; i < _imagePaths.Count; i++)
        {
            string? folder = Path.GetDirectoryName(_imagePaths[i]);
            if (!string.IsNullOrWhiteSpace(folder) &&
                string.Equals(
                    folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    _watchedImageFolder,
                    StringComparison.OrdinalIgnoreCase))
            {
                lastFolderImageIndex = i;
            }
        }

        return lastFolderImageIndex >= 0 ? lastFolderImageIndex + 1 : _imagePaths.Count;
    }

    private int FindWatchedFolderSortedInsertIndex(string imagePath, ImageViewerSortOptions sortOptions)
    {
        if (string.IsNullOrWhiteSpace(_watchedImageFolder))
            return _imagePaths.Count;

        int lastFolderImageIndex = -1;
        for (int i = 0; i < _imagePaths.Count; i++)
        {
            if (!IsPathInWatchedImageFolder(_imagePaths[i]))
                continue;

            lastFolderImageIndex = i;
            if (CompareImagePathsForSort(imagePath, _imagePaths[i], sortOptions) < 0)
                return i;
        }

        return lastFolderImageIndex >= 0 ? lastFolderImageIndex + 1 : _imagePaths.Count;
    }

    private bool IsPathInWatchedImageFolder(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(_watchedImageFolder))
            return false;

        string? folder = Path.GetDirectoryName(imagePath);
        return !string.IsNullOrWhiteSpace(folder) &&
            string.Equals(
                folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                _watchedImageFolder,
                StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareImagePathsForSort(string leftPath, string rightPath, ImageViewerSortOptions sortOptions)
    {
        if (string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (sortOptions.TaggedFilesOnTop)
        {
            bool leftTagged = TagManager.Instance.HasTags(leftPath);
            bool rightTagged = TagManager.Instance.HasTags(rightPath);
            if (leftTagged != rightTagged)
                return leftTagged ? -1 : 1;
        }

        var leftItem = CreateImageFileItemForSort(leftPath);
        var rightItem = CreateImageFileItemForSort(rightPath);
        return FileSystemService.CompareItems(leftItem, rightItem, sortOptions.Column, sortOptions.Direction);
    }

    private static FileItem CreateImageFileItemForSort(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return new FileItem
            {
                FullPath = info.FullName,
                Name = info.Name,
                IsDirectory = false,
                Size = info.Exists ? info.Length : 0,
                DateModified = info.Exists ? info.LastWriteTime : DateTime.MinValue,
                DateCreated = info.Exists ? info.CreationTime : DateTime.MinValue,
                Extension = info.Extension,
                DisplayPath = info.DirectoryName ?? ""
            };
        }
        catch
        {
            return new FileItem
            {
                FullPath = path,
                Name = Path.GetFileName(path),
                IsDirectory = false,
                Extension = Path.GetExtension(path),
                DisplayPath = Path.GetDirectoryName(path) ?? ""
            };
        }
    }

    private bool TryGetCurrentImageDisplayRect(out RectangleF imageRect)
    {
        imageRect = RectangleF.Empty;
        if (_currentImage == null)
            return false;

        float imgWidth = _currentImage.Width * _zoomLevel;
        float imgHeight = _currentImage.Height * _zoomLevel;
        float x = (_pictureBox.Width - imgWidth) / 2f + _panOffset.X;
        float y = (_pictureBox.Height - imgHeight) / 2f + _panOffset.Y;
        imageRect = new RectangleF(x, y, imgWidth, imgHeight);
        return imageRect.Width > 0f && imageRect.Height > 0f;
    }

    private bool TryGetNormalizedManualSelectionRect(Point start, Point end, out RectangleF normalizedRect)
    {
        normalizedRect = RectangleF.Empty;
        if (!TryGetCurrentImageDisplayRect(out var imageRect))
            return false;

        float left = Math.Min(start.X, end.X);
        float top = Math.Min(start.Y, end.Y);
        float right = Math.Max(start.X, end.X);
        float bottom = Math.Max(start.Y, end.Y);
        var selection = RectangleF.FromLTRB(left, top, right, bottom);
        var clipped = RectangleF.Intersect(selection, imageRect);
        if (clipped.Width < 4f || clipped.Height < 4f)
            return false;

        normalizedRect = ClampNormalizedRect(
            (clipped.X - imageRect.X) / imageRect.Width,
            (clipped.Y - imageRect.Y) / imageRect.Height,
            clipped.Width / imageRect.Width,
            clipped.Height / imageRect.Height);
        return normalizedRect.Width > 0.0025f && normalizedRect.Height > 0.0025f;
    }

    private static RectangleF RotateNormalizedRectCounterClockwise(RectangleF rect)
        => ClampNormalizedRect(rect.Y, 1f - (rect.X + rect.Width), rect.Height, rect.Width);

    private static RectangleF UnrotateNormalizedRect(RectangleF rect, int clockwiseQuarterTurns)
    {
        var result = rect;
        int turns = ((clockwiseQuarterTurns % 4) + 4) % 4;
        for (int i = 0; i < turns; i++)
            result = RotateNormalizedRectCounterClockwise(result);
        return result;
    }

    private static Rectangle NormalizeRectToPixels(RectangleF normalizedRect, Size imageSize)
    {
        int left = Math.Clamp((int)Math.Floor(normalizedRect.X * imageSize.Width), 0, Math.Max(0, imageSize.Width - 1));
        int top = Math.Clamp((int)Math.Floor(normalizedRect.Y * imageSize.Height), 0, Math.Max(0, imageSize.Height - 1));
        int right = Math.Clamp((int)Math.Ceiling((normalizedRect.X + normalizedRect.Width) * imageSize.Width), left + 1, imageSize.Width);
        int bottom = Math.Clamp((int)Math.Ceiling((normalizedRect.Y + normalizedRect.Height) * imageSize.Height), top + 1, imageSize.Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private List<OverlayTextBlock> BuildPendingManualOverlayBlocks()
    {
        var blocks = new List<OverlayTextBlock>(_pendingManualOcrRegions.Count);
        int sourceIndexBase = _overlayBlocks.Count;
        for (int i = 0; i < _pendingManualOcrRegions.Count; i++)
        {
            blocks.Add(new OverlayTextBlock
            {
                SourceIndex = sourceIndexBase + i,
                SourceText = "",
                DisplayText = "Manual OCR",
                NormalizedRect = _pendingManualOcrRegions[i].NormalizedRect,
                NormalizedFontSize = 0f,
                IsManualBox = true,
                IsPendingManualBox = true
            });
        }
        return blocks;
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
        DrawPendingManualOcrRegions(e.Graphics, imageRect);
    }

    private void DrawOverlayBlocks(Graphics g, RectangleF imageRect)
    {
        if (!_overlayToggle.Checked || _overlayBlocks.Count == 0)
            return;

        var priorHint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        try
        {
            using var badgeBrush = new SolidBrush(Color.FromArgb(220, 20, 20, 20));
            using var badgeBorder = new Pen(Color.FromArgb(220, 125, 198, 255), 1f);
            float badgeFontPx = Math.Clamp(9f * _zoomLevel, 8f, 16f);
            using var badgeFont = new Font("Segoe UI", badgeFontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textFormat = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.Word
            };

            const float textInsetX = 4f;
            const float textInsetY = 3f;
            const float minTextFontPx = 8f;
            const float minExactTextFontPx = 4f;
            const float maxTextFontPx = 34f;
            const float modelFontScale = 1.25f;
            const float maxGrowWidthFactor = 2.40f;
            const float maxGrowHeightFactor = 5.00f;
            int maxShrinkSteps = _overlayBlocks.Count > 120 ? 16 : 52;
            int maxWidenSteps = _overlayBlocks.Count > 120 ? 5 : 12;
            int maxFinalShrinkSteps = _overlayBlocks.Count > 120 ? 12 : 36;
            var placedRects = new List<RectangleF>(_overlayBlocks.Count);

            for (int i = 0; i < _overlayBlocks.Count; i++)
            {
                var block = _overlayBlocks[i];
                OverlayStyleDefaults style = GetEffectiveOverlayStyle(block);
                bool exactBox = true;
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

                        if (exactBox)
                        {
                            textFontPx = FitTextFontInsideFixedOverlay(
                                g,
                                text,
                                textFontPx,
                                minExactTextFontPx,
                                textRect,
                                textFormat);
                        }
                        else
                        {
                            float readableWidth = MeasureLongestTextTokenWidth(g, text, textFontPx) + 4f;
                            if (readableWidth > textRect.Width)
                            {
                                float maxReadableWidth = Math.Max(
                                    textRect.Width,
                                    Math.Min(imageRect.Right - textRect.X - 1f, rect.Width * maxGrowWidthFactor));
                                textRect.Width = Math.Min(readableWidth, maxReadableWidth);
                                drawRect = RectangleF.Inflate(textRect, textInsetX, textInsetY);
                            }

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

                            if (measured.Height > textRect.Height)
                            {
                                float availableTextHeight = Math.Max(8f, imageRect.Bottom - textRect.Y - 1f);
                                textRect.Height = Math.Min(availableTextHeight, measured.Height + 2f);
                                var expandedRect = RectangleF.Inflate(textRect, textInsetX, textInsetY);
                                drawRect = ShiftRectIntoBounds(expandedRect, imageRect);
                                textRect = RectangleF.Inflate(drawRect, -textInsetX, -textInsetY);
                            }
                        }
                    }
                    else
                    {
                        text = null;
                    }
                }

                drawRect = exactBox
                    ? ShiftRectIntoBounds(drawRect, imageRect)
                    : ResolveOverlayCollision(drawRect, imageRect, placedRects);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    textRect = RectangleF.Inflate(drawRect, -textInsetX, -textInsetY);
                }

                Color fillColor = style.BoxFillColorArgb.HasValue
                    ? Color.FromArgb(style.BoxFillColorArgb.Value)
                    : DefaultOverlayFillColor;
                Color borderColor = style.BoxBorderColorArgb.HasValue
                    ? Color.FromArgb(style.BoxBorderColorArgb.Value)
                    : DefaultOverlayBorderColor;
                Color textColor = style.TextColorArgb.HasValue
                    ? Color.FromArgb(style.TextColorArgb.Value)
                    : DefaultOverlayTextColor;
                Color textOutlineColor = style.TextOutlineColorArgb.HasValue
                    ? Color.FromArgb(style.TextOutlineColorArgb.Value)
                    : DefaultOverlayTextOutlineColor;
                using var fillBrush = style.BoxFillVisible == false ? null : new SolidBrush(fillColor);
                using var borderPen = style.BoxBorderVisible == false ? null : new Pen(borderColor, 1.2f);
                using var textBrush = new SolidBrush(textColor);

                if (fillBrush != null)
                    g.FillRectangle(fillBrush, drawRect);
                if (borderPen != null)
                    g.DrawRectangle(borderPen, drawRect.X, drawRect.Y, drawRect.Width, drawRect.Height);

                if (IsCurrentImageOverlayJobPending())
                {
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
                }

                if (!string.IsNullOrWhiteSpace(text) && textRect.Width > 4f && textRect.Height > 4f)
                {
                    using var textFont = new Font("Segoe UI", textFontPx, FontStyle.Bold, GraphicsUnit.Pixel);
                    DrawOverlayText(
                        g,
                        text,
                        textFont,
                        textBrush,
                        textRect,
                        style.TextAlignment ?? StringAlignment.Near,
                        style.TextVerticalAlignment ?? StringAlignment.Near,
                        style.TextOutlineVisible == true,
                        textOutlineColor);
                }

                placedRects.Add(drawRect);
            }
        }
        finally
        {
            g.TextRenderingHint = priorHint;
        }
    }

    private void DrawPendingManualOcrRegions(Graphics g, RectangleF imageRect)
    {
        string? imagePath = GetCurrentImagePath();
        List<RectangleF>? queuedRegions = null;
        bool hasQueuedRegions =
            !string.IsNullOrWhiteSpace(imagePath) &&
            _queuedManualRegionsByImage.TryGetValue(imagePath, out queuedRegions) &&
            queuedRegions.Count > 0;

        if (_pendingManualOcrRegions.Count == 0 && !_isDrawingManualOcrRegion && !hasQueuedRegions)
            return;

        using var pendingFill = new SolidBrush(Color.FromArgb(90, 76, 29, 149));
        using var pendingBorder = new Pen(Color.FromArgb(240, 193, 155, 255), 1.4f)
        {
            DashStyle = DashStyle.Dash
        };
        using var queuedFill = new SolidBrush(Color.FromArgb(70, 204, 133, 32));
        using var queuedBorder = new Pen(Color.FromArgb(240, 255, 196, 92), 1.4f)
        {
            DashStyle = DashStyle.Dash
        };
        using var previewFill = new SolidBrush(Color.FromArgb(75, 255, 255, 255));
        using var labelBrush = new SolidBrush(Color.FromArgb(230, 20, 20, 20));
        using var labelTextBrush = new SolidBrush(Color.White);
        using var labelFont = new Font("Segoe UI", Math.Clamp(9f * _zoomLevel, 8f, 16f), FontStyle.Bold, GraphicsUnit.Pixel);

        for (int i = 0; i < _pendingManualOcrRegions.Count; i++)
        {
            var region = _pendingManualOcrRegions[i];
            var rect = new RectangleF(
                imageRect.X + (region.NormalizedRect.X * imageRect.Width),
                imageRect.Y + (region.NormalizedRect.Y * imageRect.Height),
                region.NormalizedRect.Width * imageRect.Width,
                region.NormalizedRect.Height * imageRect.Height);

            if (rect.Width < 2f || rect.Height < 2f)
                continue;

            g.FillRectangle(pendingFill, rect);
            g.DrawRectangle(pendingBorder, rect.X, rect.Y, rect.Width, rect.Height);

            string label = $"Manual {i + 1}";
            var labelSize = g.MeasureString(label, labelFont);
            var labelRect = new RectangleF(
                rect.X,
                Math.Max(imageRect.Y, rect.Y - labelSize.Height - 4f),
                labelSize.Width + 8f,
                labelSize.Height + 2f);
            g.FillRectangle(labelBrush, labelRect);
            g.DrawString(label, labelFont, labelTextBrush, labelRect.X + 4f, labelRect.Y + 1f);
        }

        if (hasQueuedRegions && queuedRegions != null)
        {
            for (int i = 0; i < queuedRegions.Count; i++)
            {
                var region = queuedRegions[i];
                var rect = new RectangleF(
                    imageRect.X + (region.X * imageRect.Width),
                    imageRect.Y + (region.Y * imageRect.Height),
                    region.Width * imageRect.Width,
                    region.Height * imageRect.Height);

                if (rect.Width < 2f || rect.Height < 2f)
                    continue;

                g.FillRectangle(queuedFill, rect);
                g.DrawRectangle(queuedBorder, rect.X, rect.Y, rect.Width, rect.Height);

                string label = $"Queued {i + 1}";
                var labelSize = g.MeasureString(label, labelFont);
                var labelRect = new RectangleF(
                    rect.X,
                    Math.Max(imageRect.Y, rect.Y - labelSize.Height - 4f),
                    labelSize.Width + 8f,
                    labelSize.Height + 2f);
                g.FillRectangle(labelBrush, labelRect);
                g.DrawString(label, labelFont, labelTextBrush, labelRect.X + 4f, labelRect.Y + 1f);
            }
        }

        if (_isDrawingManualOcrRegion &&
            TryGetNormalizedManualSelectionRect(_manualOcrDragStart, _manualOcrDragCurrent, out var dragRect))
        {
            var rect = new RectangleF(
                imageRect.X + (dragRect.X * imageRect.Width),
                imageRect.Y + (dragRect.Y * imageRect.Height),
                dragRect.Width * imageRect.Width,
                dragRect.Height * imageRect.Height);
            g.FillRectangle(previewFill, rect);
            g.DrawRectangle(pendingBorder, rect.X, rect.Y, rect.Width, rect.Height);
        }
    }

    private static SizeF MeasureTextForOverlay(Graphics g, string text, float fontPx, float maxWidth, StringFormat format)
    {
        using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
        return MeasureOverlayTextLayout(g, text, font, Math.Max(1f, maxWidth)).Size;
    }

    private static float MeasureLongestTextTokenWidth(Graphics g, string text, float fontPx)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0f;

        using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
        float width = 0f;
        foreach (var token in text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var size = g.MeasureString(token, font);
            if (size.Width > width)
                width = size.Width;
        }

        return width;
    }

    private static float FitTextFontInsideFixedOverlay(
        Graphics g,
        string text,
        float startFontPx,
        float minFontPx,
        RectangleF textRect,
        StringFormat format)
    {
        if (string.IsNullOrWhiteSpace(text) || textRect.Width <= 1f || textRect.Height <= 1f)
            return Math.Max(1f, minFontPx);

        float fontPx = Math.Max(minFontPx, startFontPx);
        for (int i = 0; i < 80; i++)
        {
            using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            var layout = MeasureOverlayTextLayout(g, text, font, textRect.Width);
            SizeF measured = layout.Size;
            if (measured.Height <= textRect.Height + 0.5f &&
                measured.Width <= textRect.Width + 0.5f)
            {
                return fontPx;
            }

            if (fontPx <= minFontPx + 0.01f)
                return minFontPx;

            fontPx = Math.Max(minFontPx, fontPx - 0.75f);
        }

        return fontPx;
    }

    private static void DrawOverlayText(
        Graphics g,
        string text,
        Font font,
        Brush brush,
        RectangleF textRect,
        StringAlignment alignment,
        StringAlignment verticalAlignment,
        bool outlineVisible,
        Color outlineColor)
    {
        var layout = MeasureOverlayTextLayout(g, text, font, textRect.Width);
        if (layout.Lines.Count == 0)
            return;

        var state = g.Save();
        try
        {
            g.SetClip(textRect);
            using var lineFormat = CreateOverlayLineFormat(alignment);
            float extraHeight = Math.Max(0f, textRect.Height - layout.Size.Height);
            float y = verticalAlignment switch
            {
                StringAlignment.Center => textRect.Y + (extraHeight / 2f),
                StringAlignment.Far => textRect.Y + extraHeight,
                _ => textRect.Y
            };
            foreach (string line in layout.Lines)
            {
                if (y > textRect.Bottom)
                    break;

                var lineRect = new RectangleF(textRect.X, y, textRect.Width, layout.LineHeight);
                if (outlineVisible)
                {
                    using var path = new GraphicsPath();
                    path.AddString(line, font.FontFamily, (int)font.Style, font.Size, lineRect, lineFormat);
                    using var outlinePen = new Pen(outlineColor, Math.Max(1f, font.Size * 0.075f))
                    {
                        LineJoin = LineJoin.Round
                    };
                    g.DrawPath(outlinePen, path);
                    g.FillPath(brush, path);
                }
                else
                {
                    g.DrawString(line, font, brush, lineRect, lineFormat);
                }
                y += layout.LineHeight;
            }
        }
        finally
        {
            g.Restore(state);
        }
    }

    private static (List<string> Lines, SizeF Size, float LineHeight) MeasureOverlayTextLayout(
        Graphics g,
        string text,
        Font font,
        float maxWidth)
    {
        var lines = WrapOverlayText(g, text, font, maxWidth);
        float lineHeight = Math.Max(1f, font.GetHeight(g) * 1.05f);
        float width = 0f;
        foreach (string line in lines)
            width = Math.Max(width, MeasureOverlayLineWidth(g, font, line));

        return (lines, new SizeF(width, lines.Count * lineHeight), lineHeight);
    }

    private static List<string> WrapOverlayText(Graphics g, string text, Font font, float maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return lines;

        maxWidth = Math.Max(1f, maxWidth);
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (string rawParagraph in normalized.Split('\n'))
        {
            string paragraph = rawParagraph.Trim();
            if (paragraph.Length == 0)
            {
                lines.Add("");
                continue;
            }

            var tokens = TokenizeOverlayText(paragraph);
            string current = "";
            foreach (var token in tokens)
            {
                if (string.IsNullOrWhiteSpace(token.Text))
                    continue;

                string tokenText = !string.IsNullOrEmpty(current) && token.SpaceBefore
                    ? " " + token.Text
                    : token.Text;
                string candidate = string.IsNullOrEmpty(current) ? token.Text : current + tokenText;

                // Keep closing punctuation with the preceding text instead of starting a
                // visually awkward line on its own. Fixed-box font fitting accounts for the
                // small amount of extra width when the punctuation pushes the line over.
                if (!string.IsNullOrEmpty(current) && IsOverlayClosingPunctuationToken(token.Text))
                {
                    current = candidate;
                    continue;
                }

                if (string.IsNullOrEmpty(current) || MeasureOverlayLineWidth(g, font, candidate) <= maxWidth + 0.5f)
                {
                    if (MeasureOverlayLineWidth(g, font, candidate) <= maxWidth + 0.5f || !CanHyphenateOverlayToken(token.Text))
                    {
                        current = candidate;
                        continue;
                    }

                    var splitLines = SplitLongOverlayToken(g, font, token.Text, maxWidth);
                    if (splitLines.Count == 0)
                    {
                        current = candidate;
                        continue;
                    }

                    for (int i = 0; i < splitLines.Count - 1; i++)
                        lines.Add(splitLines[i]);
                    current = splitLines[^1];
                    continue;
                }

                lines.Add(current);
                if (MeasureOverlayLineWidth(g, font, token.Text) <= maxWidth + 0.5f || !CanHyphenateOverlayToken(token.Text))
                {
                    current = token.Text;
                    continue;
                }

                var parts = SplitLongOverlayToken(g, font, token.Text, maxWidth);
                if (parts.Count == 0)
                {
                    current = token.Text;
                    continue;
                }

                for (int i = 0; i < parts.Count - 1; i++)
                    lines.Add(parts[i]);
                current = parts[^1];
            }

            if (!string.IsNullOrEmpty(current))
                lines.Add(current);
        }

        return lines;
    }

    private readonly record struct OverlayTextToken(string Text, bool SpaceBefore);

    private static List<OverlayTextToken> TokenizeOverlayText(string text)
    {
        var tokens = new List<OverlayTextToken>();
        var word = new StringBuilder();
        bool pendingSpace = false;
        bool wordSpaceBefore = false;
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            string element = enumerator.GetTextElement();
            if (string.IsNullOrEmpty(element))
                continue;

            char ch = element[0];
            if (char.IsWhiteSpace(ch))
            {
                FlushOverlayWordToken(tokens, word, wordSpaceBefore);
                wordSpaceBefore = false;
                pendingSpace = true;
                continue;
            }

            if (IsCjkTextElement(element) || IsOverlayPunctuationToken(ch))
            {
                FlushOverlayWordToken(tokens, word, wordSpaceBefore);
                wordSpaceBefore = false;
                tokens.Add(new OverlayTextToken(element, pendingSpace));
                pendingSpace = false;
                continue;
            }

            if (word.Length == 0)
            {
                wordSpaceBefore = pendingSpace;
                pendingSpace = false;
            }
            word.Append(element);
        }

        FlushOverlayWordToken(tokens, word, wordSpaceBefore);
        return tokens;
    }

    private static void FlushOverlayWordToken(List<OverlayTextToken> tokens, StringBuilder word, bool spaceBefore)
    {
        if (word.Length == 0)
            return;

        tokens.Add(new OverlayTextToken(word.ToString(), spaceBefore));
        word.Clear();
    }

    private static bool IsCjkTextElement(string textElement)
        => textElement.Any(IsCjkChar);

    private static bool IsOverlayPunctuationToken(char ch)
        => IsCjkPunctuation(ch) ||
            IsOverlayOpeningPunctuation(ch) ||
            IsOverlayClosingPunctuation(ch) ||
            ".,!?;:".IndexOf(ch) >= 0;

    private static bool CanHyphenateOverlayToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 6)
            return false;

        return token.Any(char.IsLetterOrDigit) && !token.Any(IsCjkChar);
    }

    private static List<string> SplitLongOverlayToken(Graphics g, Font font, string token, float maxWidth)
    {
        var result = new List<string>();
        var elements = GetTextElements(token);
        if (elements.Count <= 1)
        {
            result.Add(token);
            return result;
        }

        var current = new StringBuilder();
        for (int i = 0; i < elements.Count; i++)
        {
            string element = elements[i];
            string candidate = current + element;
            bool hasMore = i < elements.Count - 1;
            string candidateForMeasure = hasMore ? candidate + "-" : candidate;

            if (current.Length > 0 && MeasureOverlayLineWidth(g, font, candidateForMeasure) > maxWidth + 0.5f)
            {
                result.Add(current + "-");
                current.Clear();
                current.Append(element);
                continue;
            }

            current.Append(element);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    private static List<string> GetTextElements(string text)
    {
        var elements = new List<string>();
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            elements.Add(enumerator.GetTextElement());
        return elements;
    }

    private static bool IsCjkPunctuation(char ch)
        => ch is >= '\u3000' and <= '\u303F'
            or >= '\uFF00' and <= '\uFFEF';

    private static bool IsOverlayOpeningPunctuation(char ch)
        => "([{（「『【〈《".IndexOf(ch) >= 0;

    private static bool IsOverlayClosingPunctuation(char ch)
        => ".,!?;:)]}、。！？）」』】〉》".IndexOf(ch) >= 0;

    private static bool IsOverlayClosingPunctuationToken(string token)
        => token.Length == 1 && IsOverlayClosingPunctuation(token[0]);

    private static float MeasureOverlayLineWidth(Graphics g, Font font, string line)
    {
        if (string.IsNullOrEmpty(line))
            return 0f;

        using var format = CreateOverlayLineFormat();
        return g.MeasureString(line, font, PointF.Empty, format).Width;
    }

    private static StringFormat CreateOverlayLineFormat(StringAlignment alignment = StringAlignment.Near)
    {
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.Alignment = alignment;
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        format.Trimming = StringTrimming.None;
        return format;
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

        float step = Math.Clamp(Math.Min(baseRect.Width, baseRect.Height) * 0.16f, 6f, 20f);
        var directions = new (float dx, float dy)[]
        {
            (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f),
            (1f, 1f), (-1f, 1f), (1f, -1f), (-1f, -1f),
            (2f, 1f), (-2f, 1f), (2f, -1f), (-2f, -1f),
            (1f, 2f), (-1f, 2f), (1f, -2f), (-1f, -2f)
        };

        RectangleF bestRect = baseRect;
        float bestPenalty = ComputeTotalOverlayOverlapPenalty(baseRect, placedRects);

        for (int ring = 1; ring <= 8; ring++)
        {
            foreach (var (dx, dy) in directions)
            {
                var shifted = new RectangleF(
                    baseRect.X + (dx * step * ring),
                    baseRect.Y + (dy * step * ring),
                    baseRect.Width,
                    baseRect.Height);
                shifted = ShiftRectIntoBounds(shifted, bounds);
                float penalty = ComputeTotalOverlayOverlapPenalty(shifted, placedRects);
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    bestRect = shifted;
                }

                if (!HasHeavyOverlayOverlap(shifted, placedRects))
                    return shifted;
            }
        }

        // If we are constrained by image bounds, gradually shrink as a last resort to reduce overlap.
        RectangleF shrinkCandidate = bestRect;
        for (int i = 0; i < 4; i++)
        {
            float newWidth = Math.Max(bounds.Width * 0.04f, shrinkCandidate.Width * 0.92f);
            float newHeight = Math.Max(bounds.Height * 0.04f, shrinkCandidate.Height * 0.92f);
            float cx = shrinkCandidate.X + (shrinkCandidate.Width * 0.5f);
            float cy = shrinkCandidate.Y + (shrinkCandidate.Height * 0.5f);
            var shrunk = new RectangleF(
                cx - (newWidth * 0.5f),
                cy - (newHeight * 0.5f),
                newWidth,
                newHeight);
            shrunk = ShiftRectIntoBounds(shrunk, bounds);

            float penalty = ComputeTotalOverlayOverlapPenalty(shrunk, placedRects);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                bestRect = shrunk;
            }

            if (!HasHeavyOverlayOverlap(shrunk, placedRects))
                return shrunk;

            shrinkCandidate = shrunk;
        }

        return bestRect;
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
            if (overlapRatio >= 0.34f)
                return true;
        }

        return false;
    }

    private static float ComputeTotalOverlayOverlapPenalty(RectangleF candidate, List<RectangleF> placedRects)
    {
        if (placedRects.Count == 0)
            return 0f;

        float candidateArea = Math.Max(1f, candidate.Width * candidate.Height);
        int start = Math.Max(0, placedRects.Count - 100);
        float total = 0f;

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
            total += overlapRatio * overlapRatio;
        }

        return total;
    }

    private void PictureBox_MouseDown(object? sender, MouseEventArgs e)
    {
        FocusViewerForHotkeys();

        if (e.Button == MouseButtons.Left)
        {
            DateTime now = DateTime.UtcNow;
            TimeSpan sinceLastRelease = now - _lastPictureBoxLeftMouseUpUtc;
            bool isSecondClick =
                sinceLastRelease.TotalMilliseconds >= 0 &&
                sinceLastRelease.TotalMilliseconds <= SystemInformation.DoubleClickTime &&
                Math.Abs(e.X - _lastPictureBoxLeftMouseUpPoint.X) <= SystemInformation.DoubleClickSize.Width &&
                Math.Abs(e.Y - _lastPictureBoxLeftMouseUpPoint.Y) <= SystemInformation.DoubleClickSize.Height;
            _pictureBoxSecondClickDownUtc = isSecondClick ? now : DateTime.MinValue;
        }

        if (e.Button == MouseButtons.Right)
        {
            _contextOverlayBlockIndex = HitTestOverlayBlock(e.Location);
            return;
        }

        if (_manualOcrDrawMode && e.Button == MouseButtons.Left)
        {
            if (TryGetCurrentImageDisplayRect(out var imageRect) && imageRect.Contains(e.Location))
            {
                _isDrawingManualOcrRegion = true;
                _manualOcrDragStart = e.Location;
                _manualOcrDragCurrent = e.Location;
                _pictureBox.Cursor = Cursors.Cross;
                _pictureBox.Invalidate();
            }
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            if (!IsCurrentImageActivelyProcessing() &&
                TryHitTestOverlayManipulation(e.Location, out int blockIndex, out var dragMode))
            {
                _overlayDragMode = dragMode;
                _overlayDragBlockIndex = blockIndex;
                _overlayDragImagePath = GetCurrentImagePath();
                _overlayDragStartPoint = e.Location;
                _overlayDragStartRect = _overlayBlocks[blockIndex].NormalizedRect;
                _overlayDragStartHadUserOverride = _overlayBlocks[blockIndex].HasUserOverride;
                _overlayDragChanged = false;
                _pictureBox.Cursor = GetOverlayDragCursor(dragMode);
                return;
            }

            _isPanning = true;
            _lastMousePos = e.Location;
            _pictureBox.Cursor = Cursors.SizeAll;
        }
    }

    private void PictureBox_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_overlayDragMode != OverlayDragMode.None)
        {
            UpdateOverlayDrag(e.Location);
            return;
        }

        if (_isDrawingManualOcrRegion)
        {
            _manualOcrDragCurrent = e.Location;
            _pictureBox.Invalidate();
            return;
        }

        if (_isPanning)
        {
            _panOffset.X += e.X - _lastMousePos.X;
            _panOffset.Y += e.Y - _lastMousePos.Y;
            _lastMousePos = e.Location;
            _pictureBox.Invalidate();
        }
        else if (_manualOcrDrawMode)
        {
            _pictureBox.Cursor = Cursors.Cross;
        }
        else if (!IsCurrentImageActivelyProcessing() &&
            TryHitTestOverlayManipulation(e.Location, out _, out var hoverMode))
        {
            _pictureBox.Cursor = GetOverlayDragCursor(hoverMode);
        }
        else
        {
            _pictureBox.Cursor = Cursors.Default;
        }
    }

    private void PictureBox_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _lastPictureBoxLeftMouseUpUtc = DateTime.UtcNow;
            _lastPictureBoxLeftMouseUpPoint = e.Location;
        }

        if (_overlayDragMode != OverlayDragMode.None && e.Button == MouseButtons.Left)
        {
            bool save = _overlayDragChanged;
            string? dragImagePath = _overlayDragImagePath;
            _overlayDragMode = OverlayDragMode.None;
            _overlayDragChanged = false;
            _pictureBox.Cursor = Cursors.Default;

            if (save && string.Equals(GetCurrentImagePath(), dragImagePath, StringComparison.OrdinalIgnoreCase))
                SaveOverlayBlockDragEdit();

            _overlayDragBlockIndex = -1;
            _overlayDragImagePath = null;
            _overlayDragStartHadUserOverride = false;
            _pictureBox.Invalidate();
            return;
        }

        if (_isDrawingManualOcrRegion && e.Button == MouseButtons.Left)
        {
            _isDrawingManualOcrRegion = false;
            if (TryGetNormalizedManualSelectionRect(_manualOcrDragStart, e.Location, out var normalizedRect))
            {
                _pendingManualOcrRegions.Add(new ManualOcrRegion { NormalizedRect = normalizedRect });
                RefreshAiStatusLabel();
            }

            UpdateManualOcrUiState();
            _pictureBox.Invalidate();
            return;
        }

        _isPanning = false;
        UpdateManualOcrUiState();
    }

    private void PictureBox_MouseDoubleClick(object? sender, MouseEventArgs e)
    {
        DateTime secondClickDown = _pictureBoxSecondClickDownUtc;
        _pictureBoxSecondClickDownUtc = DateTime.MinValue;

        if (e.Button != MouseButtons.Left || secondClickDown == DateTime.MinValue)
            return;

        // MouseDoubleClick is delivered after MouseUp. Do not treat a long-held second
        // press as a double-click just because Windows eventually raised the event.
        if ((DateTime.UtcNow - secondClickDown).TotalMilliseconds <= SystemInformation.DoubleClickTime)
            ToggleFullscreen();
    }

    private void UpdateOverlayDrag(Point currentPoint)
    {
        if (_overlayDragBlockIndex < 0 ||
            _overlayDragBlockIndex >= _overlayBlocks.Count ||
            !string.Equals(GetCurrentImagePath(), _overlayDragImagePath, StringComparison.OrdinalIgnoreCase) ||
            !TryGetCurrentImageDisplayRect(out var imageRect) ||
            imageRect.Width <= 1f ||
            imageRect.Height <= 1f)
        {
            CancelOverlayDrag();
            return;
        }

        float dx = (currentPoint.X - _overlayDragStartPoint.X) / imageRect.Width;
        float dy = (currentPoint.Y - _overlayDragStartPoint.Y) / imageRect.Height;
        var rect = _overlayDragStartRect;
        float minW = Math.Max(0.005f, 8f / imageRect.Width);
        float minH = Math.Max(0.005f, 8f / imageRect.Height);

        switch (_overlayDragMode)
        {
            case OverlayDragMode.Move:
                rect.X = Math.Clamp(rect.X + dx, 0f, Math.Max(0f, 1f - rect.Width));
                rect.Y = Math.Clamp(rect.Y + dy, 0f, Math.Max(0f, 1f - rect.Height));
                break;
            case OverlayDragMode.ResizeLeft:
                rect = ResizeOverlayRect(rect, left: dx, minW: minW, minH: minH);
                break;
            case OverlayDragMode.ResizeRight:
                rect = ResizeOverlayRect(rect, right: dx, minW: minW, minH: minH);
                break;
            case OverlayDragMode.ResizeTop:
                rect = ResizeOverlayRect(rect, top: dy, minW: minW, minH: minH);
                break;
            case OverlayDragMode.ResizeBottom:
                rect = ResizeOverlayRect(rect, bottom: dy, minW: minW, minH: minH);
                break;
            case OverlayDragMode.ResizeTopLeft:
                rect = ResizeOverlayRect(rect, left: dx, top: dy, minW: minW, minH: minH);
                break;
            case OverlayDragMode.ResizeTopRight:
                rect = ResizeOverlayRect(rect, right: dx, top: dy, minW: minW, minH: minH);
                break;
            case OverlayDragMode.ResizeBottomLeft:
                rect = ResizeOverlayRect(rect, left: dx, bottom: dy, minW: minW, minH: minH);
                break;
            case OverlayDragMode.ResizeBottomRight:
                rect = ResizeOverlayRect(rect, right: dx, bottom: dy, minW: minW, minH: minH);
                break;
        }

        rect = ClampNormalizedRect(rect.X, rect.Y, rect.Width, rect.Height);
        if (rect.Width < minW || rect.Height < minH)
            return;

        _overlayBlocks[_overlayDragBlockIndex].NormalizedRect = rect;
        _overlayBlocks[_overlayDragBlockIndex].HasUserOverride = true;
        _overlayDragChanged = true;
        _pictureBox.Invalidate();
    }

    private static RectangleF ResizeOverlayRect(
        RectangleF rect,
        float left = 0f,
        float right = 0f,
        float top = 0f,
        float bottom = 0f,
        float minW = 0.005f,
        float minH = 0.005f)
    {
        float x1 = rect.Left + left;
        float x2 = rect.Right + right;
        float y1 = rect.Top + top;
        float y2 = rect.Bottom + bottom;

        x1 = Math.Clamp(x1, 0f, 1f);
        x2 = Math.Clamp(x2, 0f, 1f);
        y1 = Math.Clamp(y1, 0f, 1f);
        y2 = Math.Clamp(y2, 0f, 1f);

        if (x2 - x1 < minW)
        {
            if (Math.Abs(left) > 0f)
                x1 = Math.Max(0f, x2 - minW);
            else
                x2 = Math.Min(1f, x1 + minW);
        }

        if (y2 - y1 < minH)
        {
            if (Math.Abs(top) > 0f)
                y1 = Math.Max(0f, y2 - minH);
            else
                y2 = Math.Min(1f, y1 + minH);
        }

        return new RectangleF(x1, y1, Math.Max(minW, x2 - x1), Math.Max(minH, y2 - y1));
    }

    private void PictureBox_MouseWheel(object? sender, MouseEventArgs e)
    {
        if (_overlayDragMode != OverlayDragMode.None)
            return;

        if ((ModifierKeys & Keys.Control) == Keys.Control)
        {
            AdjustZoom(e.Delta > 0 ? 0.1f : -0.1f, e.Location);
            return;
        }

        if (e.Delta > 0)
            ShowPrevious();
        else if (e.Delta < 0)
            ShowNext();
    }

    private void AdjustZoom(float delta)
    {
        AdjustZoom(delta, null);
    }

    private void AdjustZoom(float delta, Point? anchorPoint)
    {
        if (_currentImage == null)
            return;

        _autoFitEnabled = false;
        float newZoom = Math.Clamp(_zoomLevel + delta, 0.1f, 5.0f);
        ApplyZoom(newZoom, anchorPoint);
    }

    private void ApplyZoom(float newZoom, Point? anchorPoint)
    {
        if (_currentImage == null)
            return;

        float oldZoom = _zoomLevel;
        if (Math.Abs(newZoom - oldZoom) < 0.0001f)
            return;

        bool keepCentered = IsImageFullyVisibleAtZoom(oldZoom) && IsImageFullyVisibleAtZoom(newZoom);
        _zoomLevel = newZoom;
        SetZoomSliderValue((int)(_zoomLevel * 100));

        if (keepCentered)
        {
            _panOffset = Point.Empty;
            _pictureBox.Invalidate();
            return;
        }

        Point pivot = anchorPoint ?? new Point(_pictureBox.Width / 2, _pictureBox.Height / 2);
        float oldImgW = _currentImage.Width * oldZoom;
        float oldImgH = _currentImage.Height * oldZoom;
        float oldX = (_pictureBox.Width - oldImgW) / 2f + _panOffset.X;
        float oldY = (_pictureBox.Height - oldImgH) / 2f + _panOffset.Y;
        float mouseRelX = pivot.X - oldX;
        float mouseRelY = pivot.Y - oldY;

        float scaleFactor = newZoom / oldZoom;
        float newMouseRelX = mouseRelX * scaleFactor;
        float newMouseRelY = mouseRelY * scaleFactor;
        float expectedNewX = pivot.X - newMouseRelX;
        float expectedNewY = pivot.Y - newMouseRelY;
        float newImgW = _currentImage.Width * newZoom;
        float newImgH = _currentImage.Height * newZoom;
        _panOffset.X = (int)(expectedNewX - (_pictureBox.Width - newImgW) / 2f);
        _panOffset.Y = (int)(expectedNewY - (_pictureBox.Height - newImgH) / 2f);

        if (IsImageFullyVisibleAtZoom(newZoom))
            _panOffset = Point.Empty;

        _pictureBox.Invalidate();
    }

    private bool IsImageFullyVisibleAtZoom(float zoom)
    {
        if (_currentImage == null)
            return false;

        float imgWidth = _currentImage.Width * zoom;
        float imgHeight = _currentImage.Height * zoom;
        return imgWidth <= _pictureBox.Width && imgHeight <= _pictureBox.Height;
    }

    private void FitToWindow(bool allowUpscale = true)
    {
        ApplyFitToWindow(useSmallerDimension: false, allowUpscale);
    }

    private void FitToWindowBySmallerDimension(bool allowUpscale = true)
    {
        ApplyFitToWindow(useSmallerDimension: true, allowUpscale);
    }

    private void ApplyFitToWindow(bool useSmallerDimension, bool allowUpscale)
    {
        if (_currentImage == null)
            return;

        var scaleX = (float)_pictureBox.Width / _currentImage.Width;
        var scaleY = (float)_pictureBox.Height / _currentImage.Height;
        float fitScale = useSmallerDimension ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
        if (!allowUpscale)
            fitScale = Math.Min(1.0f, fitScale);
        _zoomLevel = Math.Clamp(fitScale, 0.1f, 5.0f);
        SetZoomSliderValue((int)(_zoomLevel * 100));
        _panOffset = Point.Empty;
        _pictureBox.Invalidate();
        _autoFitEnabled = true;
        _autoFitBySmallerDimension = useSmallerDimension;
    }

    private void ActualSize()
    {
        _autoFitEnabled = false;
        _autoFitBySmallerDimension = false;
        _zoomLevel = 1.0f;
        SetZoomSliderValue(100);
        _panOffset = Point.Empty;
        _pictureBox.Invalidate();
    }

    private void ShowPrevious()
    {
        if (_overlayDragMode != OverlayDragMode.None)
            return;

        if (_currentIndex > 0)
        {
            _currentIndex--;
            LoadCurrentImage();
        }
    }

    private void ShowNext()
    {
        if (_overlayDragMode != OverlayDragMode.None)
            return;

        if (_currentIndex < _imagePaths.Count - 1)
        {
            _currentIndex++;
            LoadCurrentImage();
        }
    }

    private void ToggleOverlayBoxes()
    {
        _overlayToggle.Checked = !_overlayToggle.Checked;
        if (!_aiBusy)
            _aiStatusLabel.Text = _overlayToggle.Checked ? "OCR boxes shown" : "OCR boxes hidden";
    }

    private void ToggleSavedTranslation()
    {
        if (!_showSavedOcrCheck.Checked)
        {
            if (!_aiBusy)
                _aiStatusLabel.Text = "Enable saved OCR display first";
            return;
        }

        _showSavedTranslationCheck.Checked = !_showSavedTranslationCheck.Checked;
    }

    private static bool IsHotkeyPressed(string action, Keys keyData)
    {
        if (!AppSettings.Current.Hotkeys.TryGetValue(action, out var bindingText) || string.IsNullOrWhiteSpace(bindingText))
            return false;

        try
        {
            var converted = new KeysConverter().ConvertFromString(bindingText);
            if (converted is not Keys parsed)
                return false;

            return NormalizeHotkeyKeyData(keyData) == NormalizeHotkeyBinding(parsed);
        }
        catch
        {
            return false;
        }
    }

    private static Keys NormalizeHotkeyKeyData(Keys keyData)
    {
        var code = keyData & Keys.KeyCode;
        var mods = Control.ModifierKeys & (Keys.Control | Keys.Shift | Keys.Alt);
        return code | mods;
    }

    private static Keys NormalizeHotkeyBinding(Keys binding)
    {
        var code = binding & Keys.KeyCode;
        var mods = binding & (Keys.Control | Keys.Shift | Keys.Alt);
        return code | mods;
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
            _isFullscreen = true;
        }

        ApplyChromeVisibility();
        if (_autoFitEnabled)
        {
            if (_autoFitBySmallerDimension)
                FitToWindowBySmallerDimension();
            else
                FitToWindow();
        }
    }

    private static bool IsAnyHotkeyPressed(Keys keyData, params string[] actions)
    {
        foreach (var action in actions)
        {
            if (IsHotkeyPressed(action, keyData))
                return true;
        }
        return false;
    }

    private void ApplyChromeVisibility()
    {
        Padding = _isFullscreen ? Padding.Empty : WindowFramePadding;
        _controlPanel.Visible = !_isFullscreen;
        _titleBar.Visible = !_isFullscreen;
        Invalidate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveImageViewerAiSettings();
        SaveWindowState();
        base.OnFormClosing(e);
    }

    private void SaveImageViewerAiSettings()
    {
        _settings.ImageViewerAiPanelVisible = _aiPanel.Visible;
        _settings.ImageViewerTargetLanguage = _targetLanguageBox.Text;
        _settings.ImageViewerSourceLanguageHint = _sourceLanguageHintBox.Text;
        _settings.ImageViewerOcrHint = _ocrHintBox.Text;
        _settings.ImageViewerTranslationContextHint = _translationContextHintBox.Text;
        _settings.ImageViewerManualMaxEffortTranslation = _manualMaxEffortCheck.Checked;
        _settings.ImageViewerOverlayBoxesVisible = _overlayToggle.Checked;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ClearAnimationState();
        _imageFolderRefreshTimer.Stop();
        _imageFolderRefreshTimer.Dispose();
        _imageFolderWatcher?.Dispose();
        _imageFolderWatcher = null;
        _animationTimer.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FitToWindow(allowUpscale: false);
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
