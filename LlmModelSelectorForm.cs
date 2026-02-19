using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedExplorer;

public sealed class LlmModelSelectorForm : Form
{
    [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    private readonly LlmService _service;
    private readonly string _apiUrl;
    private readonly LlmUsageKind _usageKind;
    private readonly LlmTaskKind _taskKind;
    private readonly string? _preferredModel;
    private readonly Label _infoLabel;
    private readonly Label _statusLabel;
    private readonly ListView _modelList;
    private readonly Button _refreshButton;
    private readonly Button _loadButton;
    private readonly Button _unloadButton;
    private readonly Button _useButton;
    private readonly Button _cancelButton;
    private readonly CheckBox _setDefaultCheckBox;
    private readonly bool _showLoadUnloadControls;

    private List<LlmModelInfo> _models = new();
    private bool _busy;

    public string? SelectedModelId { get; private set; }

    private static readonly Color Bg = Color.FromArgb(45, 45, 48);
    private static readonly Color PanelBg = Color.FromArgb(37, 37, 40);
    private static readonly Color ListBg = Color.FromArgb(30, 30, 32);
    private static readonly Color Fg = Color.FromArgb(235, 235, 235);
    private static readonly Color MutedFg = Color.FromArgb(170, 170, 170);
    private static readonly Color BtnBg = Color.FromArgb(64, 64, 68);
    private static readonly Color BtnPrimaryBg = Color.FromArgb(0, 120, 212);

    public LlmModelSelectorForm(
        LlmService service,
        string apiUrl,
        LlmUsageKind usageKind,
        LlmTaskKind taskKind,
        string? preferredModel,
        string title,
        string infoText,
        bool showLoadUnloadControls = true)
    {
        _service = service;
        _apiUrl = apiUrl;
        _usageKind = usageKind;
        _taskKind = taskKind;
        _preferredModel = preferredModel;
        _showLoadUnloadControls = showLoadUnloadControls;

        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Width = 920;
        Height = 540;
        BackColor = Bg;
        ForeColor = Fg;

        _infoLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(10, 10, 10, 6),
            Text = infoText,
            BackColor = PanelBg,
            ForeColor = Fg
        };

        _statusLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            Padding = new Padding(10, 4, 10, 0),
            BackColor = PanelBg,
            ForeColor = MutedFg,
            Text = "Ready"
        };

        _modelList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            BackColor = ListBg,
            ForeColor = Fg,
            BorderStyle = BorderStyle.FixedSingle
        };
        _modelList.Columns.Add("Model", 560);
        _modelList.Columns.Add("Loaded", 120);
        _modelList.Columns.Add("Vision", 90);
        _modelList.Columns.Add("Instances", 120);
        _modelList.SelectedIndexChanged += (s, e) => UpdateButtons();
        _modelList.DoubleClick += async (s, e) => await UseSelectedAsync();

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(10, 6, 10, 6),
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = PanelBg
        };

        _cancelButton = new Button { Text = "Cancel", Width = 96 };
        _cancelButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        _useButton = new Button { Text = "Use Selected", Width = 120 };
        _useButton.Click += async (s, e) => await UseSelectedAsync();

        _unloadButton = new Button { Text = "Unload Selected", Width = 120, Visible = _showLoadUnloadControls };
        _unloadButton.Click += async (s, e) => await UnloadSelectedAsync();

        _loadButton = new Button { Text = "Load Selected", Width = 110, Visible = _showLoadUnloadControls };
        _loadButton.Click += async (s, e) => await LoadSelectedAsync();

        _refreshButton = new Button { Text = "Refresh", Width = 90 };
        _refreshButton.Click += async (s, e) => await RefreshModelsAsync();

        _setDefaultCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = BuildSetDefaultLabel(),
            ForeColor = Fg,
            BackColor = PanelBg,
            Margin = new Padding(6, 10, 24, 0),
            Checked = false
        };

        buttonPanel.Controls.Add(_cancelButton);
        buttonPanel.Controls.Add(_useButton);
        if (_showLoadUnloadControls)
        {
            buttonPanel.Controls.Add(_unloadButton);
            buttonPanel.Controls.Add(_loadButton);
        }
        buttonPanel.Controls.Add(_refreshButton);
        buttonPanel.Controls.Add(_setDefaultCheckBox);
        StyleButton(_cancelButton, isPrimary: false);
        StyleButton(_useButton, isPrimary: true);
        StyleButton(_unloadButton, isPrimary: false);
        StyleButton(_loadButton, isPrimary: false);
        StyleButton(_refreshButton, isPrimary: false);

        Controls.Add(_modelList);
        Controls.Add(buttonPanel);
        Controls.Add(_statusLabel);
        Controls.Add(_infoLabel);

        _modelList.HandleCreated += (s, e) => SetWindowTheme(_modelList.Handle, "DarkMode_Explorer", null);
        Shown += async (s, e) => await RefreshModelsAsync();
    }

    private LlmModelInfo? SelectedModel
    {
        get
        {
            if (_modelList.SelectedItems.Count == 0)
                return null;
            return _modelList.SelectedItems[0].Tag as LlmModelInfo;
        }
    }

    private async Task RefreshModelsAsync(string? preferredSelection = null, bool skipBusyGuard = false)
    {
        if (_busy && !skipBusyGuard)
            return;

        if (!skipBusyGuard)
            SetBusy(true, "Fetching models...");
        try
        {
            var catalog = await _service.GetModelCatalogAsync(_apiUrl);
            _models = catalog.AvailableModels
                .Where(m => _taskKind != LlmTaskKind.Vision || m.IsVision)
                .OrderByDescending(m => m.IsLoaded)
                .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _modelList.BeginUpdate();
            _modelList.Items.Clear();

            foreach (var model in _models)
            {
                string loadedText = model.IsLoaded ? "Yes" : "No";
                string visionText = model.IsVision ? "Yes" : "No";
                string instancesText = model.LoadedInstanceIds.Count.ToString();

                var item = new ListViewItem(new[]
                {
                    model.Id,
                    loadedText,
                    visionText,
                    instancesText
                })
                {
                    Tag = model
                };
                _modelList.Items.Add(item);
            }

            _modelList.EndUpdate();

            SelectBestModel(preferredSelection);
            _statusLabel.Text = $"Models: {_models.Count} | Loaded: {_models.Count(m => m.IsLoaded)}";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Failed to fetch models: {ex.Message}";
        }
        finally
        {
            if (!skipBusyGuard)
                SetBusy(false);
            UpdateButtons();
        }
    }

    private void SelectBestModel(string? preferredSelection)
    {
        string? desired = preferredSelection;
        if (string.IsNullOrWhiteSpace(desired))
            desired = _preferredModel;

        int index = -1;
        if (!string.IsNullOrWhiteSpace(desired))
        {
            index = _models.FindIndex(m => string.Equals(m.Id, desired, StringComparison.OrdinalIgnoreCase));
        }

        if (index < 0)
            index = _models.FindIndex(m => m.IsLoaded);

        if (index < 0 && _models.Count > 0)
            index = 0;

        if (index >= 0 && index < _modelList.Items.Count)
        {
            _modelList.Items[index].Selected = true;
            _modelList.Items[index].Focused = true;
            _modelList.EnsureVisible(index);
        }
    }

    private async Task LoadSelectedAsync()
    {
        if (_busy)
            return;

        var model = SelectedModel;
        if (model == null)
            return;

        SetBusy(true, $"Loading '{model.Id}'...");
        try
        {
            await _service.LoadModelAsync(_apiUrl, model.Id);
            await WaitForModelLoadedStateAsync(model.Id, expectedLoaded: true, timeoutMs: 10000);
            await RefreshModelsAsync(model.Id, skipBusyGuard: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load model '{model.Id}': {ex.Message}", "Model Load", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateButtons();
        }
    }

    private async Task UnloadSelectedAsync()
    {
        if (_busy)
            return;

        var model = SelectedModel;
        if (model == null)
            return;

        if (model.LoadedInstanceIds.Count == 0)
        {
            MessageBox.Show(this, "Selected model has no loaded instances.", "Model Unload", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Unload all instances for '{model.Id}' ({model.LoadedInstanceIds.Count})?",
            "Model Unload",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
            return;

        SetBusy(true, $"Unloading '{model.Id}'...");
        try
        {
            foreach (var instanceId in model.LoadedInstanceIds.ToList())
            {
                await _service.UnloadModelInstanceAsync(_apiUrl, instanceId);
            }

            await WaitForModelLoadedStateAsync(model.Id, expectedLoaded: false, timeoutMs: 8000);
            await RefreshModelsAsync(model.Id, skipBusyGuard: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to unload model '{model.Id}': {ex.Message}", "Model Unload", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateButtons();
        }
    }

    private async Task UseSelectedAsync()
    {
        if (_busy)
            return;

        var model = SelectedModel;
        if (model == null)
            return;

        if (!model.IsLoaded)
        {
            var loadDecision = MessageBox.Show(
                this,
                $"Model '{model.Id}' is not loaded.\nLoad it now?",
                "Use Model",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (loadDecision != DialogResult.Yes)
                return;

            SetBusy(true, $"Loading '{model.Id}'...");
            try
            {
                await _service.LoadModelAsync(_apiUrl, model.Id);
                await WaitForModelLoadedStateAsync(model.Id, expectedLoaded: true, timeoutMs: 10000);
                await RefreshModelsAsync(model.Id, skipBusyGuard: true);
                model = SelectedModel;
                if (model == null || !model.IsLoaded)
                {
                    MessageBox.Show(this, "Model did not report as loaded after load request.", "Use Model", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                string failedModelId = model?.Id ?? "(unknown)";
                MessageBox.Show(this, $"Failed to load model '{failedModelId}': {ex.Message}", "Use Model", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                SetBusy(false);
                UpdateButtons();
            }
        }

        SelectedModelId = model.Id;

        if (_setDefaultCheckBox.Checked)
        {
            try
            {
                SaveSelectedModelAsDefault(model.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to save model default: {ex.Message}", "Model Default", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        if (status != null)
            _statusLabel.Text = status;
        UpdateButtons();
    }

    private static void StyleButton(Button button, bool isPrimary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = isPrimary ? Color.FromArgb(20, 132, 230) : Color.FromArgb(82, 82, 88);
        button.FlatAppearance.MouseDownBackColor = isPrimary ? Color.FromArgb(0, 95, 175) : Color.FromArgb(54, 54, 58);
        button.BackColor = isPrimary ? BtnPrimaryBg : BtnBg;
        button.ForeColor = Color.White;
        button.Cursor = Cursors.Hand;
    }

    private async Task<bool> WaitForModelLoadedStateAsync(string modelId, bool expectedLoaded, int timeoutMs)
    {
        const int intervalMs = 500;
        int elapsedMs = 0;

        while (elapsedMs <= timeoutMs)
        {
            try
            {
                var catalog = await _service.GetModelCatalogAsync(_apiUrl);
                var model = catalog.AvailableModels.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
                bool isLoaded = model?.IsLoaded == true;
                if (isLoaded == expectedLoaded)
                    return true;
            }
            catch
            {
                // Best-effort polling.
            }

            await Task.Delay(intervalMs);
            elapsedMs += intervalMs;
        }

        return false;
    }

    private void UpdateButtons()
    {
        var model = SelectedModel;
        bool hasSelection = model != null;
        bool loaded = hasSelection && model!.IsLoaded;
        bool hasLoadedInstances = hasSelection && model!.LoadedInstanceIds.Count > 0;

        _refreshButton.Enabled = !_busy;
        _loadButton.Enabled = !_busy && hasSelection && !loaded;
        _unloadButton.Enabled = !_busy && hasSelection && hasLoadedInstances;
        _useButton.Enabled = !_busy && hasSelection;
        _cancelButton.Enabled = !_busy;
        _modelList.Enabled = !_busy;
        _setDefaultCheckBox.Enabled = !_busy && hasSelection;
    }

    private string BuildSetDefaultLabel()
    {
        if (_usageKind == LlmUsageKind.Batch && _taskKind == LlmTaskKind.Vision)
            return "Set as default (Batch Vision)";

        if (_usageKind == LlmUsageKind.Batch && _taskKind == LlmTaskKind.Text)
            return "Set as default (Batch Text)";

        if (_taskKind == LlmTaskKind.Vision)
            return "Set as default (Assistant Vision)";

        return "Set as default (Assistant)";
    }

    private void SaveSelectedModelAsDefault(string modelId)
    {
        var settings = AppSettings.Current;

        if (_usageKind == LlmUsageKind.Batch && _taskKind == LlmTaskKind.Vision)
            settings.LlmBatchVisionModelName = modelId;
        else
            settings.LlmModelName = modelId;

        settings.Save();
    }
}
