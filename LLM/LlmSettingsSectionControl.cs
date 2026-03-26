using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SpeedExplorer;

public sealed class LlmSettingsSectionControl : UserControl
{
    private readonly FlowLayoutPanel _panel;
    private readonly CheckBox _llmEnabledChk;
    private readonly CheckBox _llmBatchProcessingChk;
    private readonly TextBox _llmApiUrlBox;
    private readonly CheckBox _llmChatEnabledChk;
    private readonly TextBox _llmChatApiUrlBox;
    private readonly ComboBox _llmModelComboBox;
    private readonly ComboBox _llmBatchVisionModelComboBox;
    private readonly NumericUpDown _llmMaxTokensNum;
    private readonly NumericUpDown _llmAgentMaxLoopsNum;
    private readonly NumericUpDown _llmTempNum;
    private readonly NumericUpDown _llmVisionMaxMpNum;
    private readonly CheckBox _llmDebugLogChk;
    private readonly Button _fetchModelsBtn;

    private int Scale(int pixels) => (int)(pixels * (DeviceDpi / 96.0));

    public LlmSettingsSectionControl()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.Transparent;
        Margin = new Padding(0);

        _panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        Controls.Add(_panel);

        var llmHeader = CreateLabel(Localization.T("llm_section"), Point.Empty);
        ApplySectionHeaderStyle(llmHeader);
        _panel.Controls.Add(llmHeader);

        _llmEnabledChk = CreateCheckBox(Localization.T("llm_enable"), Point.Empty);
        _panel.Controls.Add(_llmEnabledChk);

        _llmBatchProcessingChk = CreateCheckBox(Localization.T("llm_batch_enable"), Point.Empty);
        _panel.Controls.Add(_llmBatchProcessingChk);

        _panel.Controls.Add(CreateLabel(Localization.T("api_url"), Point.Empty));
        _llmApiUrlBox = new TextBox
        {
            Width = Scale(300),
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.LightGray,
            BorderStyle = BorderStyle.FixedSingle
        };
        _panel.Controls.Add(_llmApiUrlBox);
        _panel.Controls.Add(CreateSpacer(10));

        _llmChatEnabledChk = new CheckBox
        {
            Text = Localization.T("chat_enable"),
            AutoSize = true,
            ForeColor = Color.White,
            Margin = new Padding(0, Scale(10), 0, Scale(5)),
            BackColor = Color.Transparent
        };
        _panel.Controls.Add(_llmChatEnabledChk);

        _panel.Controls.Add(CreateLabel(Localization.T("chat_api_url"), Point.Empty));
        _llmChatApiUrlBox = new TextBox
        {
            Width = Scale(300),
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.LightGray,
            BorderStyle = BorderStyle.FixedSingle
        };
        _panel.Controls.Add(_llmChatApiUrlBox);
        _panel.Controls.Add(CreateSpacer(10));

        var modelPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        modelPanel.Controls.Add(CreateLabel(Localization.T("model_name"), new Point(0, Scale(5))));

        _llmModelComboBox = new ComboBox
        {
            Width = Scale(200),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DropDownStyle = ComboBoxStyle.DropDown
        };
        modelPanel.Controls.Add(_llmModelComboBox);

        _fetchModelsBtn = new Button
        {
            Text = Localization.T("fetch_models"),
            AutoSize = true
        };
        SettingsButtonStyle.ApplyPrimary(_fetchModelsBtn);
        _fetchModelsBtn.Click += FetchModels_Click;
        modelPanel.Controls.Add(_fetchModelsBtn);
        _panel.Controls.Add(modelPanel);

        var batchVisionModelPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        batchVisionModelPanel.Controls.Add(CreateLabel(Localization.T("batch_vision_model_name"), new Point(0, Scale(5))));

        _llmBatchVisionModelComboBox = new ComboBox
        {
            Width = Scale(200),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DropDownStyle = ComboBoxStyle.DropDown
        };
        batchVisionModelPanel.Controls.Add(_llmBatchVisionModelComboBox);
        _panel.Controls.Add(batchVisionModelPanel);

        var tokenPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        tokenPanel.Controls.Add(CreateLabel(Localization.T("max_tokens"), new Point(0, Scale(5))));
        _llmMaxTokensNum = CreateNumeric(100, 32000, Point.Empty);
        _llmMaxTokensNum.Width = Scale(100);
        tokenPanel.Controls.Add(_llmMaxTokensNum);
        _panel.Controls.Add(tokenPanel);

        var tempPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        tempPanel.Controls.Add(CreateLabel(Localization.T("temperature"), new Point(0, Scale(5))));
        _llmTempNum = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 2,
            DecimalPlaces = 2,
            Increment = 0.1M,
            Location = Point.Empty,
            Size = new Size(Scale(80), Scale(25)),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        tempPanel.Controls.Add(_llmTempNum);
        _panel.Controls.Add(tempPanel);

        var visionPixelsPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        visionPixelsPanel.Controls.Add(CreateLabel(Localization.T("vision_max_pixel_budget_mp"), new Point(0, Scale(5))));
        _llmVisionMaxMpNum = new NumericUpDown
        {
            Minimum = 0.25M,
            Maximum = 32.0M,
            DecimalPlaces = 2,
            Increment = 0.25M,
            Location = Point.Empty,
            Size = new Size(Scale(90), Scale(25)),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        visionPixelsPanel.Controls.Add(_llmVisionMaxMpNum);
        _panel.Controls.Add(visionPixelsPanel);

        var loopPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        loopPanel.Controls.Add(CreateLabel("Max Agent Loops", new Point(0, Scale(5))));
        _llmAgentMaxLoopsNum = CreateNumeric(1, 100, Point.Empty);
        _llmAgentMaxLoopsNum.Width = Scale(60);
        loopPanel.Controls.Add(_llmAgentMaxLoopsNum);
        _panel.Controls.Add(loopPanel);

        _llmDebugLogChk = CreateCheckBox(Localization.T("debug_llm_log"), Point.Empty);
        _panel.Controls.Add(_llmDebugLogChk);

        _panel.Controls.Add(CreateSpacer(16));
    }

    public void LoadFromSettings(AppSettings settings)
    {
        _llmEnabledChk.Checked = settings.LlmEnabled;
        _llmBatchProcessingChk.Checked = settings.LlmBatchProcessingEnabled;
        _llmApiUrlBox.Text = settings.LlmApiUrl;
        _llmChatEnabledChk.Checked = settings.ChatModeEnabled;
        _llmChatApiUrlBox.Text = settings.LlmChatApiUrl;
        _llmModelComboBox.Text = settings.LlmModelName;
        _llmBatchVisionModelComboBox.Text = settings.LlmBatchVisionModelName;
        _llmMaxTokensNum.Value = Math.Clamp(settings.LlmMaxTokens, 100, 32000);
        _llmTempNum.Value = (decimal)Math.Clamp(settings.LlmTemperature, 0, 2.0);
        decimal visionMp = (decimal)Math.Max(settings.LlmVisionMaxPixels, 256 * 256) / 1_000_000M;
        _llmVisionMaxMpNum.Value = Math.Clamp(visionMp, _llmVisionMaxMpNum.Minimum, _llmVisionMaxMpNum.Maximum);
        _llmAgentMaxLoopsNum.Value = Math.Clamp(settings.LlmAgentMaxLoops, 1, 100);
        _llmDebugLogChk.Checked = settings.DebugLlmLogging;
    }

    public void ApplyToSettings(AppSettings settings)
    {
        settings.LlmEnabled = _llmEnabledChk.Checked;
        settings.LlmBatchProcessingEnabled = _llmBatchProcessingChk.Checked;
        settings.LlmApiUrl = _llmApiUrlBox.Text.Trim();
        settings.ChatModeEnabled = _llmChatEnabledChk.Checked;
        settings.LlmChatApiUrl = _llmChatApiUrlBox.Text.Trim();
        settings.LlmModelName = _llmModelComboBox.Text.Trim();
        settings.LlmBatchVisionModelName = _llmBatchVisionModelComboBox.Text.Trim();
        settings.LlmMaxTokens = (int)_llmMaxTokensNum.Value;
        settings.LlmTemperature = (double)_llmTempNum.Value;
        settings.LlmVisionMaxPixels = Math.Max(256 * 256, (int)Math.Round((double)_llmVisionMaxMpNum.Value * 1_000_000.0));
        settings.LlmAgentMaxLoops = (int)_llmAgentMaxLoopsNum.Value;
        settings.DebugLlmLogging = _llmDebugLogChk.Checked;
    }

    private async void FetchModels_Click(object? sender, EventArgs e)
    {
        _fetchModelsBtn.Enabled = false;
        _fetchModelsBtn.Text = "Fetching...";
        try
        {
            var service = new LlmService { ApiUrl = _llmApiUrlBox.Text.Trim() };
            var catalog = await service.GetModelCatalogAsync(_llmApiUrlBox.Text.Trim());
            var models = catalog.AvailableModels.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var visionModels = catalog.AvailableModels
                .Where(m => m.IsVision)
                .Select(m => m.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string currentAssistant = _llmModelComboBox.Text.Trim();
            string currentBatchVision = _llmBatchVisionModelComboBox.Text.Trim();

            _llmModelComboBox.Items.Clear();
            _llmBatchVisionModelComboBox.Items.Clear();

            if (models.Count > 0)
            {
                _llmModelComboBox.Items.AddRange(models.ToArray());
                if (!string.IsNullOrWhiteSpace(currentAssistant) &&
                    !models.Contains(currentAssistant, StringComparer.OrdinalIgnoreCase))
                {
                    _llmModelComboBox.Items.Add(currentAssistant);
                }

                _llmModelComboBox.Text = string.IsNullOrWhiteSpace(currentAssistant) ? models[0] : currentAssistant;
                _llmModelComboBox.DroppedDown = true;

                if (visionModels.Count > 0)
                {
                    _llmBatchVisionModelComboBox.Items.AddRange(visionModels.ToArray());
                    if (!string.IsNullOrWhiteSpace(currentBatchVision) &&
                        !visionModels.Contains(currentBatchVision, StringComparer.OrdinalIgnoreCase))
                    {
                        _llmBatchVisionModelComboBox.Items.Add(currentBatchVision);
                    }

                    _llmBatchVisionModelComboBox.Text = string.IsNullOrWhiteSpace(currentBatchVision) ? visionModels[0] : currentBatchVision;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(currentBatchVision))
                    {
                        _llmBatchVisionModelComboBox.Items.Add(currentBatchVision);
                        _llmBatchVisionModelComboBox.Text = currentBatchVision;
                    }
                    MessageBox.Show("No vision-capable models were detected. Load a vision model in LM Studio and fetch again.", "Fetch Models", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            else
            {
                MessageBox.Show("No models found.", "Fetch Models", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to fetch models: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _fetchModelsBtn.Text = Localization.T("fetch_models");
            _fetchModelsBtn.Enabled = true;
        }
    }

    private void ApplySectionHeaderStyle(Label label)
    {
        label.Font = new Font("Segoe UI Semibold", 10);
        label.ForeColor = Color.White;
        label.Margin = new Padding(0, Scale(6), 0, Scale(4));
    }

    private Panel CreateSpacer(int height) => new() { Height = Scale(height), Width = Scale(1), BackColor = Color.Transparent };

    private Label CreateLabel(string text, Point loc) => new()
    {
        Text = text,
        Location = loc,
        AutoSize = true,
        Font = new Font("Segoe UI", 10),
        ForeColor = Color.FromArgb(240, 240, 240),
        BackColor = Color.Transparent
    };

    private NumericUpDown CreateNumeric(int min, int max, Point loc) => new()
    {
        Minimum = min,
        Maximum = max,
        Location = loc,
        Size = new Size(Scale(80), Scale(25)),
        BackColor = Color.FromArgb(60, 60, 60),
        ForeColor = Color.White
    };

    private CheckBox CreateCheckBox(string text, Point loc) => new()
    {
        Text = text,
        Location = loc,
        AutoSize = true,
        Font = new Font("Segoe UI", 10),
        ForeColor = Color.FromArgb(240, 240, 240),
        Cursor = Cursors.Hand,
        BackColor = Color.Transparent
    };
}
