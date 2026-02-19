using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedExplorer;

public class BatchProcessingForm : Form
{
    private readonly List<string> _files;
    private readonly IntPtr _ownerHandle;
    private readonly LlmService _llmService;
    
    private ComboBox _promptSelector = null!;
    private TextBox _promptInput = null!;
    private Button _startButton = null!;
    private Button _stopButton = null!;
    private Button _closeButton = null!; // For when finished
    private Button _restoreDefaultsButton = null!;
    private ProgressBar _progressBar = null!;
    private Label _statusLabel = null!;
    private TextBox _logBox = null!;

    private CancellationTokenSource? _cts;
    private bool _isProcessing = false;

    private int Scale(int pixels) => (int)(pixels * (this.DeviceDpi / 96.0));
    private Padding Scale(Padding p) => new Padding(Scale(p.Left), Scale(p.Top), Scale(p.Right), Scale(p.Bottom));

    public BatchProcessingForm(List<string> files, IntPtr ownerHandle)
    {
        _files = files;
        _ownerHandle = ownerHandle;
        _llmService = new LlmService();
        _llmService.ApiUrl = AppSettings.Current.LlmApiUrl; // Ensure init

        InitializeComponent();
        LoadPrompts();
    }

    private void InitializeComponent()
    {
        this.Text = Localization.T("batch_title");
        this.Size = new Size(Scale(600), Scale(500));
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(30, 30, 30);
        this.ForeColor = Color.LightGray;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = Scale(new Padding(15))
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(70))); // Header/Dropdown + Mode
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(100))); // Prompt Box
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(40))); // Buttons
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(30))); // Progress
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Log

        // 1. Prompt Selection & Mode
        var headerPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true
        };
        
        var presetLabel = new Label { Text = Localization.T("batch_preset"), AutoSize = true, Margin = Scale(new Padding(0, 6, 5, 0)) };
        _promptSelector = new ComboBox
        {
            Width = Scale(200),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White
        };
        _promptSelector.SelectedIndexChanged += PromptSelector_SelectedIndexChanged;

        _restoreDefaultsButton = new Button
        {
            Text = Localization.T("batch_reset_prompts"),
            Width = Scale(100),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 40, 40),
            ForeColor = Color.LightSalmon,
            Margin = Scale(new Padding(10, 0, 0, 0))
        };
        _restoreDefaultsButton.Click += RestoreDefaults_Click;

        headerPanel.Controls.Add(presetLabel);
        headerPanel.Controls.Add(_promptSelector);
        headerPanel.Controls.Add(_restoreDefaultsButton);

        // Mode Selection Panel
        var modePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = Scale(new Padding(0, 5, 0, 0))
        };
        _rbGeneral = new RadioButton { Text = Localization.T("batch_mode_general"), Checked = true, AutoSize = true, ForeColor = Color.White };
        _rbTagging = new RadioButton { Text = Localization.T("batch_mode_tagging"), AutoSize = true, ForeColor = Color.White };
        
        modePanel.Controls.Add(_rbGeneral);
        modePanel.Controls.Add(_rbTagging);

        var topContainer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Height = Scale(70)
        };
        topContainer.Controls.Add(headerPanel, 0, 0);
        topContainer.Controls.Add(modePanel, 0, 1);

        // 2. Prompt Input
        _promptInput = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Vertical
        };

        // 3. Buttons
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };

        _startButton = new Button
        {
            Text = Localization.T("batch_start"),
            Width = Scale(120),
            Height = Scale(35),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.CornflowerBlue,
            ForeColor = Color.White
        };
        _startButton.Click += StartButton_Click;

        _stopButton = new Button
        {
            Text = Localization.T("batch_stop"),
            Width = Scale(80),
            Height = Scale(35),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.IndianRed,
            ForeColor = Color.White,
            Enabled = false
        };
        _stopButton.Click += StopButton_Click;

        _closeButton = new Button
        {
            Text = Localization.T("batch_close"),
            Width = Scale(80),
            Height = Scale(35),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60)
        };
        _closeButton.Click += (s, e) => this.Close();

        buttonPanel.Controls.Add(_closeButton);
        buttonPanel.Controls.Add(_startButton);
        buttonPanel.Controls.Add(_stopButton);

        // 4. Progress
        var progressPanel = new Panel { Dock = DockStyle.Fill };
        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 10,
            Style = ProgressBarStyle.Continuous
        };
        _statusLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = Scale(20),
            Text = string.Format(Localization.T("batch_ready"), _files.Count),
            ForeColor = Color.DarkGray
        };
        progressPanel.Controls.Add(_statusLabel);
        progressPanel.Controls.Add(_progressBar);

        // 5. Log
        _logBox = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.Gray,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9)
        };

        mainLayout.Controls.Add(topContainer, 0, 0);
        mainLayout.Controls.Add(_promptInput, 0, 1);
        mainLayout.Controls.Add(buttonPanel, 0, 2);
        mainLayout.Controls.Add(progressPanel, 0, 3);
        mainLayout.Controls.Add(_logBox, 0, 4);

        this.Controls.Add(mainLayout);
    }
    
    private RadioButton _rbGeneral = null!;
    private RadioButton _rbTagging = null!;

    private void LoadPrompts()
    {
        _promptSelector.Items.Clear();
        foreach (var kvp in PromptManager.Instance.BatchPrompts)
        {
            _promptSelector.Items.Add(kvp.Key);
        }
        
        if (_promptSelector.Items.Count > 0)
            _promptSelector.SelectedIndex = 0;
    }

    private void PromptSelector_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_promptSelector.SelectedItem is string key && 
            PromptManager.Instance.BatchPrompts.TryGetValue(key, out var prompt))
        {
            _promptInput.Text = prompt;
            // Auto-switch mode based on key?
            if (key.Contains("Tag", StringComparison.OrdinalIgnoreCase))
                _rbTagging.Checked = true;
            else
                _rbGeneral.Checked = true;
        }
    }

    private void RestoreDefaults_Click(object? sender, EventArgs e)
    {
        if (MessageBox.Show("Reset all prompts to defaults?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
        {
            PromptManager.Instance.ResetToDefaults();
            LoadPrompts();
        }
    }

    private async void StartButton_Click(object? sender, EventArgs e)
    {
        if (_isProcessing) return;

        var prompt = _promptInput.Text.Trim();
        if (string.IsNullOrEmpty(prompt))
        {
            MessageBox.Show("Please enter a prompt.");
            return;
        }

        // Save customized prompt back to manager? Maybe complex logic.
        // For now, let's just proceed.

        _isProcessing = true;
        _cts = new CancellationTokenSource();
        
        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        _promptInput.Enabled = false;
        _promptSelector.Enabled = false;
        
        _progressBar.Maximum = _files.Count;
        _progressBar.Value = 0;
        _logBox.Clear();
        Log(Localization.T("batch_starting"));

        var settings = AppSettings.Current;
        string batchApiUrl = LlmService.GetCompletionsApiUrl(settings.LlmApiUrl, settings.LlmChatApiUrl);
        _llmService.ApiUrl = batchApiUrl;
        if (!string.Equals(batchApiUrl, settings.LlmApiUrl, StringComparison.OrdinalIgnoreCase))
        {
            Log($"Using batch API endpoint: {batchApiUrl}");
        }

        try
        {
            bool hasImageFiles = _files.Any(FileSystemService.IsImageFile);
            bool hasNonImageFiles = _files.Any(f => !FileSystemService.IsImageFile(f));

            string? batchVisionModel = null;
            string? batchTextModel = null;

            if (_rbTagging.Checked || hasImageFiles)
            {
                Log("Resolving vision model...");
                batchVisionModel = await _llmService.ResolveModelForTaskAsync(LlmUsageKind.Batch, LlmTaskKind.Vision, this);
                if (string.IsNullOrWhiteSpace(batchVisionModel))
                {
                    Log("Cancelled: no vision model selected.");
                    return;
                }
                Log($"Vision model: {batchVisionModel}");
            }

            if (!_rbTagging.Checked && hasNonImageFiles)
            {
                Log("Resolving text model...");
                batchTextModel = await _llmService.ResolveModelForTaskAsync(LlmUsageKind.Batch, LlmTaskKind.Text, this);
                if (string.IsNullOrWhiteSpace(batchTextModel))
                {
                    Log("Cancelled: no text model selected.");
                    return;
                }
                Log($"Text model: {batchTextModel}");
            }

            for (int i = 0; i < _files.Count; i++)
            {
                if (_cts.Token.IsCancellationRequested)
                {
                    Log("Processing cancelled by user.");
                    break;
                }

                string file = _files[i];
                string fileName = Path.GetFileName(file);
                
                Log($"[{i + 1}/{_files.Count}] Processing {fileName}...");
                _statusLabel.Text = $"Processing {i + 1} of {_files.Count}: {fileName}";

                try
                {
                    // Keep batch processing pinned to completions endpoint (not chat history endpoint).
                    _llmService.ApiUrl = batchApiUrl;
                    
                    bool isImageFile = FileSystemService.IsImageFile(file);
                    List<string>? imagePaths = isImageFile ? new List<string> { file } : null;
                    string dir = Path.GetDirectoryName(file)!;

                    if (_rbTagging.Checked)
                    {
                        // TAGGING MODE
                        if (!isImageFile)
                        {
                            Log("  -> Skipped (not an image file).");
                        }
                        else
                        {
                            var tags = await _llmService.GetImageTagsAsync(prompt, file, batchVisionModel);
                            if (tags.Count > 0)
                            {
                                Log($"  -> Generated {tags.Count} tags: {string.Join(", ", tags)}");
                                // Apply tags
                                TagManager.Instance.UpdateTagsBatch(new[] { file }, tags, Enumerable.Empty<string>());
                                Log($"  -> Tags applied.");
                            }
                            else
                            {
                                Log("  -> No tags generated.");
                            }
                        }
                    }
                    else
                    {
                        // GENERAL MODE
                        // Inject naming convention if user prompt is asking for description
                        // Or just universally useful hint for create_file
                        string augmentedPrompt = prompt + 
                            $"\n\nTarget File: {fileName}" +
                            $"\nIMPORTANT: If you create a text file for a description, you MUST name it '{Path.GetFileNameWithoutExtension(fileName)}_desc.txt'.";

                        string? selectedModel = isImageFile
                            ? (batchVisionModel ?? batchTextModel)
                            : (batchTextModel ?? batchVisionModel);

                        if (string.IsNullOrWhiteSpace(selectedModel))
                        {
                            Log("  -> ERROR: No model available for this file type.");
                        }
                        else
                        {
                            string response = await _llmService.SendPromptAsync(
                                augmentedPrompt, 
                                dir, 
                                false, // fullContext off
                                settings.LlmTaggingEnabled, 
                                settings.LlmSearchEnabled, 
                                settings.LlmThinkingEnabled,
                                imagePaths,
                                selectedModel
                            );

                            var commands = LlmService.ParseCommands(response);

                            if (commands.Count > 0)
                            {
                                Log($"  -> Executing {commands.Count} commands...");
                                var executor = new LlmExecutor(dir, _ownerHandle);
                                
                                // HACK: Pre-pend a 'select_files' command for this file to ensure context is set!
                                commands.Insert(0, new LlmCommand { 
                                    Cmd = "select_files", 
                                    Files = new List<string> { fileName } 
                                });

                                var ops = executor.ExecuteCommands(commands);
                                Log($"  -> Success. {ops.Count} operations recorded.");
                            }
                            else
                            {
                                Log("  -> No commands returned.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"  -> ERROR: {ex.Message}");
                    LlmDebugLogger.LogError(ex.ToString());
                }

                _progressBar.Value = i + 1;
            }
        }
        catch (Exception ex)
        {
            Log($"Critical Error: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
            _startButton.Enabled = true;
            _stopButton.Enabled = false;
            _promptInput.Enabled = true;
            _promptSelector.Enabled = true;
            _statusLabel.Text = "Done.";
            _cts = null;
        }
    }

    private void StopButton_Click(object? sender, EventArgs e)
    {
        if (_isProcessing && _cts != null)
        {
            _cts.Cancel();
            _stopButton.Enabled = false;
            Log(Localization.T("batch_stopping"));
        }
    }

    private void Log(string message)
    {
        if (IsDisposed) return;
        _logBox.AppendText(message + Environment.NewLine);
    }
}
