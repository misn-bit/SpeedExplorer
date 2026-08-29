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

    }
}
