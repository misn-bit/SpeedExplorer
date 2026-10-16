using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SpeedExplorer.Design;

namespace SpeedExplorer.LLM;

/// <summary>
/// LLM settings page: endpoint URLs, model pickers (with a fetch-from-server button),
/// and numeric limits. Loads/saves directly against AppSettings.
/// </summary>
internal sealed class LlmSettingsSectionControl : SettingsPage
{
    private readonly ToggleSwitch _llmEnabledChk, _llmBatchProcessingChk, _llmChatEnabledChk, _llmDebugLogChk;
    private readonly TextField _llmApiUrlBox, _llmChatApiUrlBox;
    private readonly Dropdown _llmModelComboBox, _llmBatchVisionModelComboBox;
    private readonly SliderField _llmMaxTokensSlider, _llmTempSlider, _llmVisionMaxMpSlider, _llmAgentMaxLoopsSlider;
    private readonly FlatButton _fetchModelsBtn;

    public LlmSettingsSectionControl() : base(Localization.T("llm_tab"))
    {
        Font = new Font("Segoe UI", 10);

        var group = new SettingsGroup();
        _llmEnabledChk = group.AddToggle(Localization.T("llm_enable"), string.Empty, false, _ => { });
        _llmBatchProcessingChk = group.AddToggle(Localization.T("llm_batch_enable"), string.Empty, false, _ => { });
        _llmChatEnabledChk = group.AddToggle(Localization.T("chat_enable"), string.Empty, false, _ => { });
        _llmDebugLogChk = group.AddToggle(Localization.T("debug_llm_log"), string.Empty, false, _ => { });

        _llmApiUrlBox = group.AddText(Localization.T("api_url"), string.Empty, string.Empty, "http://host:port/v1");
        _llmChatApiUrlBox = group.AddText(Localization.T("chat_api_url"), string.Empty, string.Empty, "http://host:port/v1");

        _llmModelComboBox = group.AddDropdown(
            Localization.T("model_name"), string.Empty, null, "Select or type a model…", "No models fetched yet.", _ => { });

        _fetchModelsBtn = group.AddButton(
            string.Empty, string.Empty, Localization.T("fetch_models"), ButtonKind.Primary, FetchModels_Click);

        _llmBatchVisionModelComboBox = group.AddDropdown(
            Localization.T("batch_vision_model_name"), string.Empty, null, "Select or type a model…", "No models fetched yet.", _ => { });

        _llmMaxTokensSlider = group.AddSlider(
            Localization.T("max_tokens"), string.Empty, 100, 32000, 100, 4096, " tok", _ => { });
        _llmTempSlider = group.AddSlider(
            Localization.T("temperature"), string.Empty, 0, 20, 1, 10, "/10", _ => { });
        _llmVisionMaxMpSlider = group.AddSlider(
            Localization.T("vision_max_pixel_budget_mp"), string.Empty, 6, 6400, 2, 256, "/100 MP", _ => { });
        _llmAgentMaxLoopsSlider = group.AddSlider(
            Localization.T("agent_max_loops"), string.Empty, 1, 100, 1, 10, " loops", _ => { });

        Controls.Add(group);
    }

    public void LoadFromSettings(AppSettings settings)
    {
        _llmEnabledChk.Checked = settings.LlmEnabled;
        _llmBatchProcessingChk.Checked = settings.LlmBatchProcessingEnabled;
        _llmApiUrlBox.Value = settings.LlmApiUrl;
        _llmChatEnabledChk.Checked = settings.ChatModeEnabled;
        _llmChatApiUrlBox.Value = settings.LlmChatApiUrl;
        _llmModelComboBox.Value = settings.LlmModelName;
        _llmBatchVisionModelComboBox.Value = settings.LlmBatchVisionModelName;
        _llmMaxTokensSlider.Value = Math.Clamp(settings.LlmMaxTokens, 100, 32000);
        _llmTempSlider.Value = (int)Math.Round(Math.Clamp(settings.LlmTemperature, 0, 2.0) * 10);
        decimal visionMp = (decimal)Math.Max(settings.LlmVisionMaxPixels, 256 * 256) / 1_000_000M;
        _llmVisionMaxMpSlider.Value = (int)Math.Clamp(visionMp * 100, 6, 6400);
        _llmAgentMaxLoopsSlider.Value = Math.Clamp(settings.LlmAgentMaxLoops, 1, 100);
        _llmDebugLogChk.Checked = settings.DebugLlmLogging;
    }

    public void ApplyToSettings(AppSettings settings)
    {
        settings.LlmEnabled = _llmEnabledChk.Checked;
        settings.LlmBatchProcessingEnabled = _llmBatchProcessingChk.Checked;
        settings.LlmApiUrl = _llmApiUrlBox.Value.Trim();
        settings.ChatModeEnabled = _llmChatEnabledChk.Checked;
        settings.LlmChatApiUrl = _llmChatApiUrlBox.Value.Trim();
        settings.LlmModelName = _llmModelComboBox.Value?.Trim() ?? string.Empty;
        settings.LlmBatchVisionModelName = _llmBatchVisionModelComboBox.Value?.Trim() ?? string.Empty;
        settings.LlmMaxTokens = _llmMaxTokensSlider.Value;
        settings.LlmTemperature = _llmTempSlider.Value / 10.0;
        settings.LlmVisionMaxPixels = Math.Max(256 * 256, (int)Math.Round(_llmVisionMaxMpSlider.Value / 100.0 * 1_000_000.0));
        settings.LlmAgentMaxLoops = _llmAgentMaxLoopsSlider.Value;
        settings.DebugLlmLogging = _llmDebugLogChk.Checked;
    }

    private async void FetchModels_Click()
    {
        _fetchModelsBtn.Enabled = false;
        string oldText = _fetchModelsBtn.Text;
        _fetchModelsBtn.Text = "Fetching...";
        try
        {
            var service = new LlmService { ApiUrl = _llmApiUrlBox.Value.Trim() };
            var catalog = await service.GetModelCatalogAsync(_llmApiUrlBox.Value.Trim());
            var models = catalog.AvailableModels.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var visionModels = catalog.AvailableModels
                .Where(m => m.IsVision)
                .Select(m => m.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string currentAssistant = _llmModelComboBox.Value?.Trim() ?? string.Empty;
            string currentBatchVision = _llmBatchVisionModelComboBox.Value?.Trim() ?? string.Empty;

            if (models.Count > 0)
            {
                var items = new System.Collections.Generic.List<string>(models);
                if (!string.IsNullOrWhiteSpace(currentAssistant) &&
                    !models.Contains(currentAssistant, StringComparer.OrdinalIgnoreCase))
                    items.Add(currentAssistant);
                _llmModelComboBox.SetItems(items);
                _llmModelComboBox.Value = string.IsNullOrWhiteSpace(currentAssistant) ? models[0] : currentAssistant;

                if (visionModels.Count > 0)
                {
                    var visionItems = new System.Collections.Generic.List<string>(visionModels);
                    if (!string.IsNullOrWhiteSpace(currentBatchVision) &&
                        !visionModels.Contains(currentBatchVision, StringComparer.OrdinalIgnoreCase))
                        visionItems.Add(currentBatchVision);
                    _llmBatchVisionModelComboBox.SetItems(visionItems);
                }
                _llmBatchVisionModelComboBox.Value =
                    string.IsNullOrWhiteSpace(currentBatchVision) ? null : currentBatchVision;
            }
        }
        catch
        {
            // keep whatever was there; the URL row is where the user fixes the endpoint
        }
        finally
        {
            _fetchModelsBtn.Enabled = true;
            _fetchModelsBtn.Text = oldText;
        }
    }
}
