using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private void InitializeAiPanel()
    {
        // --- AI Panel (Image Viewer) ---
        _aiPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = Scale(360),
            BackColor = Color.FromArgb(28, 28, 28),
            Padding = Scale(new Padding(8)),
            Visible = false
        };

        _aiActionRow = new FlowLayoutPanel
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
        _aiActionRow.Controls.Add(_ocrBtn);
        _aiActionRow.Controls.Add(_translateBtn);
        _aiActionRow.Controls.Add(_drawOcrBoxBtn);
        _aiActionRow.Controls.Add(_clearManualOcrBoxesBtn);
        _aiActionRow.Controls.Add(_tagBtn);

        _aiLangRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = Scale(28),
            BackColor = Color.Transparent
        };
        _targetLanguageLabel = new Label
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
        _aiLangRow.Controls.Add(_targetLanguageLabel);
        _aiLangRow.Controls.Add(_targetLanguageBox);

        _aiSourceHintRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = Scale(28),
            BackColor = Color.Transparent
        };
        _sourceLanguageLabel = new Label
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
        _aiSourceHintRow.Controls.Add(_sourceLanguageLabel);
        _aiSourceHintRow.Controls.Add(_sourceLanguageHintBox);

        _aiOcrHintRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = Scale(46),
            BackColor = Color.Transparent
        };
        _ocrHintLabel = new Label
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
        _aiOcrHintRow.Controls.Add(_ocrHintLabel);
        _aiOcrHintRow.Controls.Add(_ocrHintBox);

        _aiContextHintRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = Scale(46),
            BackColor = Color.Transparent
        };
        _contextHintLabel = new Label
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
        _aiContextHintRow.Controls.Add(_contextHintLabel);
        _aiContextHintRow.Controls.Add(_translationContextHintBox);

        _aiManualModeRow = new FlowLayoutPanel
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
        _aiManualModeRow.Controls.Add(_manualMaxEffortCheck);

        _aiReasoningRow = new FlowLayoutPanel
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
        _aiReasoningRow.Controls.Add(_ocrReasoningCheck);
        _aiReasoningRow.Controls.Add(_translationReasoningCheck);

        _aiToolsRow = new FlowLayoutPanel
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

        _aiToolsRow.Controls.Add(_overlayToggle);
        _aiToolsRow.Controls.Add(_copyResultBtn);
        _aiToolsRow.Controls.Add(_cancelCurrentJobBtn);
        _aiToolsRow.Controls.Add(_abortBtn);

        _aiSavedToggleRow = new FlowLayoutPanel
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
        _aiSavedToggleRow.Controls.Add(_showSavedOcrCheck);
        _aiSavedToggleRow.Controls.Add(_showSavedTranslationCheck);

        _aiSavedActionRow = new FlowLayoutPanel
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
        _aiSavedActionRow.Controls.Add(_openSavedOcrFileBtn);
        _aiSavedActionRow.Controls.Add(_deleteSavedTranslationBtn);
        _aiSavedActionRow.Controls.Add(_clearOverlayBtn);

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
        _aiPanel.Controls.Add(_aiSavedActionRow);
        _aiPanel.Controls.Add(_aiSavedToggleRow);
        _aiPanel.Controls.Add(_aiStatusLabel);
        _aiPanel.Controls.Add(_aiToolsRow);
        _aiPanel.Controls.Add(_aiReasoningRow);
        _aiPanel.Controls.Add(_aiManualModeRow);
        _aiPanel.Controls.Add(_aiContextHintRow);
        _aiPanel.Controls.Add(_aiOcrHintRow);
        _aiPanel.Controls.Add(_aiSourceHintRow);
        _aiPanel.Controls.Add(_aiLangRow);
        _aiPanel.Controls.Add(_aiActionRow);
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

    }
}
