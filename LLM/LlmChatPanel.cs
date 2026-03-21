using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Text.Json;
using System.ComponentModel;

namespace SpeedExplorer;

/// <summary>
/// Custom TextBox that prevents Space key from being intercepted by parent form's hotkeys.
/// </summary>
public class LlmInputTextBox : TextBox
{
    protected override bool IsInputKey(Keys keyData)
    {
        if (keyData == Keys.Space)
            return true; // Handle space as input, not a hotkey
        return base.IsInputKey(keyData);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Don't let parent form capture space or other normal typing keys
        if (keyData == Keys.Space || (keyData >= Keys.A && keyData <= Keys.Z) || 
            (keyData >= Keys.D0 && keyData <= Keys.D9))
        {
            return false; // Let the textbox handle it
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

/// <summary>
/// Chat panel UI for LLM interaction. Sits below the file list.
/// </summary>
public class LlmChatPanel : Panel
{
    private readonly TextBox _inputBox;
    private Panel _inputBoxPanel;
    private readonly Button _sendButton;
    private readonly Label _statusLabel;
    private readonly LlmService _llmService;
    
    private readonly CheckBox _fullContextToggle;
    private readonly CheckBox _taggingToggle;
    private readonly CheckBox _searchToggle;
    private readonly CheckBox _thinkingToggle;
    private readonly CheckBox _agentToggle;
    
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string>? GetCurrentDirectory { get; set; }
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<IntPtr>? GetOwnerHandle { get; set; }
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action? OnOperationsComplete { get; set; }

    public bool IsInputFocused => _inputBox.Focused || (_historyBox != null && _historyBox.Focused);
    public bool IsExpanded => _isExpanded;

    private int Scale(int pixels) => (int)(pixels * (this.DeviceDpi / 96.0));
    private Padding Scale(Padding p) => new Padding(Scale(p.Left), Scale(p.Top), Scale(p.Right), Scale(p.Bottom));

    public void UpdateLayoutForScale()
    {
        _inputPanel.Height = Scale(74);
        this.Height = _inputPanel.Height;
        this.Padding = Padding.Empty;
        
        if (this.Controls.Count > 1 && this.Controls[1] is FlowLayoutPanel header)
        {
            header.Height = Scale(24);
            header.Margin = Scale(new Padding(0));
            header.Padding = Scale(new Padding(0, 2, 0, 0));
            
            foreach (Control c in header.Controls)
            {
                if (c is Label lbl && lbl.Name != "") // Status label
                {
                    lbl.Width = Scale(90);
                    lbl.Height = Scale(20);
                    lbl.Margin = Scale(new Padding(0, 0, 10, 0));
                }
                else if (c is CheckBox cb)
                {
                    cb.Margin = Scale(new Padding(0, 1, 15, 0));
                }
            }
        }

        if (this.Controls.Count > 0 && this.Controls[0] is Panel inputPanel)
        {
            inputPanel.Padding = Scale(new Padding(0, 2, 0, 0));
            if (inputPanel.Controls.Count > 1 && inputPanel.Controls[1] is Button sendBtn)
            {
                sendBtn.Width = Scale(60);
            }
        }
        SyncSendButtonHeight();
        if (_isExpanded)
            UpdateHistoryOverlayBounds();
    }


    private Panel _historyContainer;
    private RichTextBox _historyBox;
    private Panel _dragHandle;
    private Panel _inputPanel;
    private bool _isResizing;
    private int _lastHeight = 0;
    private List<ChatMessage> _chatHistory = new();
    private Button _clearHistoryBtn;
    private bool _allowExpandOnFocus;
    private bool _isExpanded;
    private Control? _overlayParent;
    private DateTime _autoCollapseBlockedUntilUtc = DateTime.MinValue;

    public LlmChatPanel()
    {
        _llmService = new LlmService();
        
        this.Height = Scale(74);
        this.Dock = DockStyle.Bottom;
        this.BackColor = Color.FromArgb(35, 35, 35);
        this.Padding = Padding.Empty; // Reset padding for custom layout
        this.Visible = AppSettings.Current.LlmEnabled;

        // 1. Drag Handle (Top)
        _dragHandle = new Panel
        {
            Dock = DockStyle.Top,
            Height = Scale(4),
            Cursor = Cursors.SizeNS,
            BackColor = Color.FromArgb(50, 50, 50)
        };
        _dragHandle.MouseDown += (s, e) => { _isResizing = true; };
        _dragHandle.MouseMove += DragHandle_MouseMove;
        _dragHandle.MouseUp += (s, e) => { _isResizing = false; };
        this.Controls.Add(_dragHandle);

        // 2. History Panel (Fill, initially hidden/collapsed conceptually via height)
        _historyContainer = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Scale(new Padding(8)),
            Visible = false // Hidden by default until expanded
        };
        
        _historyBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.LightGray,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            Font = new Font("Segoe UI", 10),
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        _historyContainer.Controls.Add(_historyBox);
        this.Controls.Add(_historyContainer);

        // 3. Input Panel (Bottom)
        _inputPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = Scale(74), // Fixed height for input area
            Padding = Scale(new Padding(8, 6, 8, 8))
        };

        // Header (Toggles) inside Input Panel
        var headerPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = Scale(24),
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Scale(new Padding(0)),
            Padding = Scale(new Padding(0, 2, 0, 0))
        };

        _statusLabel = new Label
        {
            Text = Localization.T("ai_assistant"),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Width = Scale(90),
            Height = Scale(20),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Scale(new Padding(0, 0, 10, 0))
        };

        _fullContextToggle = CreateToggle(Localization.T("ai_full_context"), Localization.T("ai_full_context_desc"));
        _taggingToggle = CreateToggle(Localization.T("ai_tagging"), Localization.T("ai_tagging_desc"));
        _searchToggle = CreateToggle(Localization.T("ai_search"), Localization.T("ai_search_desc"));
        _thinkingToggle = CreateToggle(Localization.T("ai_thinking"), Localization.T("ai_thinking_desc"));
        _agentToggle = CreateToggle("Agent Mode", "Enables autonomous multi-step reasoning loops.");

        var s = AppSettings.Current;
        _fullContextToggle.Checked = s.LlmFullContextEnabled;
        _taggingToggle.Checked = s.LlmTaggingEnabled;
        _searchToggle.Checked = s.LlmSearchEnabled;
        _thinkingToggle.Checked = s.LlmThinkingEnabled;
        _agentToggle.Checked = s.LlmAgentModeEnabled;

        // Wire persistence
        _fullContextToggle.CheckedChanged += (src, e) => { AppSettings.Current.LlmFullContextEnabled = _fullContextToggle.Checked; AppSettings.Current.Save(); };
        _taggingToggle.CheckedChanged += (src, e) => { AppSettings.Current.LlmTaggingEnabled = _taggingToggle.Checked; AppSettings.Current.Save(); };
        _searchToggle.CheckedChanged += (src, e) => { AppSettings.Current.LlmSearchEnabled = _searchToggle.Checked; AppSettings.Current.Save(); };
        _thinkingToggle.CheckedChanged += (src, e) => { AppSettings.Current.LlmThinkingEnabled = _thinkingToggle.Checked; AppSettings.Current.Save(); };
        _agentToggle.CheckedChanged += (src, e) => { AppSettings.Current.LlmAgentModeEnabled = _agentToggle.Checked; AppSettings.Current.Save(); };

        // Clear History Button
        _clearHistoryBtn = new Button
        {
            Text = "🗑",
            Width = Scale(30),
            Height = Scale(20),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = Color.Gray,
            Cursor = Cursors.Hand,
            Margin = Scale(new Padding(5, 0, 0, 0)),
            TextAlign = ContentAlignment.MiddleCenter
        };
        _clearHistoryBtn.FlatAppearance.BorderSize = 0;
        _clearHistoryBtn.Click += (sender, e) => ClearHistory();
        new ToolTip().SetToolTip(_clearHistoryBtn, Localization.T("ai_clear_history"));

        headerPanel.Controls.Add(_statusLabel);
        headerPanel.Controls.Add(_fullContextToggle);
        headerPanel.Controls.Add(_taggingToggle);
        headerPanel.Controls.Add(_searchToggle);
        headerPanel.Controls.Add(_thinkingToggle);
        headerPanel.Controls.Add(_agentToggle);
        headerPanel.Controls.Add(_clearHistoryBtn);

        // Input Box & Send Button
        _inputBoxPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Scale(new Padding(0, 2, 0, 0))
        };

        _sendButton = new Button
        {
            Text = Localization.T("ai_send"),
            Width = Scale(60),
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.LightGray,
            Cursor = Cursors.Hand,
            Height = Scale(26)
        };
        _sendButton.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
        _sendButton.Click += SendButton_Click;

        _inputBox = new LlmInputTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.LightGray,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10)
        };
        _inputBox.KeyDown += InputBox_KeyDown;
        _inputBox.MouseDown += (src, e) => _allowExpandOnFocus = true;
        _inputBox.GotFocus += (src, e) => OnInputFocus();
        _inputBox.LostFocus += (src, e) => OnInputBlur();
        _inputBox.SizeChanged += (s, e) => SyncSendButtonHeight();

        _inputBoxPanel.Controls.Add(_inputBox);
        _inputBoxPanel.Controls.Add(_sendButton);

        _inputPanel.Controls.Add(_inputBoxPanel);
        _inputPanel.Controls.Add(headerPanel);
        
        this.Controls.Add(_inputPanel);
        
        this.LostFocus += (s, e) => ScheduleCollapseCheck();
        _historyBox.LostFocus += (s, e) => ScheduleCollapseCheck();
        _clearHistoryBtn.LostFocus += (s, e) => ScheduleCollapseCheck();
        this.ParentChanged += (s, e) => HookOverlayParent();
        this.Disposed += (s, e) =>
        {
            if (_overlayParent != null)
                _overlayParent.Resize -= OverlayParent_Resize;
        };

        UpdateLayoutForMode();
    }

    private void SyncSendButtonHeight()
    {
        if (_sendButton == null || _inputBox == null) return;
        _sendButton.Height = Math.Max(Scale(24), _inputBox.Height);
    }

    private void DragHandle_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_isResizing && e.Button == MouseButtons.Left && this.Parent != null)
        {
            // Mouse moves up -> larger expanded overlay.
            int delta = -e.Y;
            int minHeight = _inputPanel.Height + Scale(80);
            int maxHeight = (int)(this.Parent.Height * 0.9);
            int seed = _lastHeight > 0 ? _lastHeight : _inputPanel.Height;
            int newHeight = Math.Clamp(seed + delta, minHeight, maxHeight);
            _lastHeight = newHeight;
            AppSettings.Current.LlmChatPanelHeightRatio = (double)newHeight / this.Parent.Height;
            UpdateHistoryOverlayBounds();
        }
    }

    private void OnInputFocus()
    {
        if (AppSettings.Current.ChatModeEnabled)
        {
            bool shouldExpand = _allowExpandOnFocus || IsCursorOverInput();
            _allowExpandOnFocus = false;
            if (shouldExpand)
                ExpandPanel();
        }
    }

    private void OnInputBlur()
    {
        _allowExpandOnFocus = false;
        ScheduleCollapseCheck();
    }

    private void ScheduleCollapseCheck()
    {
        BeginInvoke(new Action(() =>
        {
            if (DateTime.UtcNow < _autoCollapseBlockedUntilUtc)
                return;
            if (!HasAnyChatFocus())
            {
                CollapsePanel();
            }
        }));
    }

    private void ExpandPanel()
    {
        if (this.Parent == null) return;

        _isExpanded = true;
        _dragHandle.Visible = true;
        _historyContainer.Visible = true;
        HookOverlayParent();
        EnsureHistoryOverlayParent();

        int targetHeight = (int)(this.Parent.Height * AppSettings.Current.LlmChatPanelHeightRatio);
        targetHeight = Math.Max(targetHeight, _inputPanel.Height * 2);
        _lastHeight = targetHeight;
        this.Height = _inputPanel.Height;
        UpdateHistoryOverlayBounds();
    }

    private void CollapsePanel()
    {
        _isExpanded = false;
        _historyContainer.Visible = false;
        _dragHandle.Visible = false;
        this.Height = _inputPanel.Height;
        _allowExpandOnFocus = false;
        ReturnHistoryContainerToSelf();
    }

    private void ClearHistory()
    {
        _chatHistory.Clear();
        _llmService.ClearChatSession();
        _historyBox.Clear();
        
        string systemPrompt = LlmPromptBuilder.GetChatSystemPrompt(_taggingToggle.Checked, _searchToggle.Checked, _fullContextToggle.Checked, _thinkingToggle.Checked);
        _chatHistory.Add(new ChatMessage { Role = "system", Content = systemPrompt });
        
        AppendMessage("System", Localization.T("ai_history_cleared"), Color.Gray);
        _statusLabel.Text = Localization.T("ai_assistant");
    }

    private void AppendMessage(string role, string text, Color color)
    {
        if (_historyBox.InvokeRequired)
        {
            _historyBox.BeginInvoke(new Action(() => AppendMessage(role, text, color)));
            return;
        }

        _historyBox.SelectionStart = _historyBox.TextLength;
        _historyBox.SelectionLength = 0;
        
        _historyBox.SelectionColor = color;
        _historyBox.SelectionFont = new Font(_historyBox.Font, FontStyle.Bold);
        _historyBox.AppendText($"{role}: ");
        
        _historyBox.SelectionColor = Color.LightGray;
        _historyBox.SelectionFont = _historyBox.Font;
        _historyBox.AppendText($"{text}\n\n");
        _historyBox.ScrollToCaret();
    }

    private void UpdateLayoutForMode()
    {
        bool chatMode = AppSettings.Current.ChatModeEnabled;
        _clearHistoryBtn.Visible = chatMode;
        
        if (!chatMode)
        {
             CollapsePanel();
             _historyContainer.Visible = false;
             _dragHandle.Visible = false;
        }
        else
        {
            // If in chat mode but not focused, it will be collapsed by focus logic
            // But we ensure it starts in correct state
            if (!HasAnyChatFocus()) CollapsePanel();
        }
    }

    private void HookOverlayParent()
    {
        if (_overlayParent == this.Parent)
            return;

        if (_overlayParent != null)
            _overlayParent.Resize -= OverlayParent_Resize;

        _overlayParent = this.Parent;

        if (_overlayParent != null)
            _overlayParent.Resize += OverlayParent_Resize;
    }

    private void OverlayParent_Resize(object? sender, EventArgs e)
    {
        if (_isExpanded)
            UpdateHistoryOverlayBounds();
    }

    private void EnsureHistoryOverlayParent()
    {
        if (this.Parent == null)
            return;
        if (_historyContainer.Parent == this.Parent)
            return;

        try
        {
            _historyContainer.Parent?.Controls.Remove(_historyContainer);
            _historyContainer.Dock = DockStyle.None;
            this.Parent.Controls.Add(_historyContainer);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
    }

    private void ReturnHistoryContainerToSelf()
    {
        if (_historyContainer.Parent == this)
            return;
        try
        {
            _historyContainer.Parent?.Controls.Remove(_historyContainer);
            _historyContainer.Dock = DockStyle.Fill;
            this.Controls.Add(_historyContainer);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
    }

    private void UpdateHistoryOverlayBounds()
    {
        if (!_isExpanded || this.Parent == null)
            return;

        EnsureHistoryOverlayParent();
        if (_historyContainer.Parent != this.Parent)
            return;

        int parentHeight = this.Parent.Height;
        int minTotal = _inputPanel.Height + Scale(80);
        int maxTotal = (int)(parentHeight * 0.9);
        int totalHeight = _lastHeight > 0 ? _lastHeight : (int)(parentHeight * AppSettings.Current.LlmChatPanelHeightRatio);
        totalHeight = Math.Clamp(totalHeight, minTotal, Math.Max(minTotal, maxTotal));
        _lastHeight = totalHeight;

        int overlayHeight = Math.Max(Scale(60), totalHeight - _inputPanel.Height);
        int y = this.Top - overlayHeight;
        if (y < 0)
        {
            overlayHeight += y;
            y = 0;
        }
        if (overlayHeight < Scale(20))
            overlayHeight = Scale(20);

        _historyContainer.Bounds = new Rectangle(this.Left, y, this.Width, overlayHeight);
        _historyContainer.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _historyContainer.BringToFront();
    }

    private bool HasAnyChatFocus()
    {
        return this.ContainsFocus ||
               (_historyContainer != null && _historyContainer.ContainsFocus) ||
               (_historyBox != null && _historyBox.Focused) ||
               (_clearHistoryBtn != null && _clearHistoryBtn.Focused);
    }

    private CheckBox CreateToggle(string text, string tooltip)
    {
        var cb = new CheckBox
        {
            Text = text,
            ForeColor = Color.DarkGray,
            Font = new Font("Segoe UI", 8),
            AutoSize = true,
            Margin = Scale(new Padding(0, 0, 15, 0)),
            Cursor = Cursors.Hand
        };
        
        var tt = new ToolTip();
        tt.SetToolTip(cb, tooltip);
        
        cb.CheckedChanged += (s, e) =>
        {
            cb.ForeColor = cb.Checked ? Color.LightGray : Color.DarkGray;
        };
        
        return cb;
    }

    public void UpdateFromSettings()
    {
        this.Visible = AppSettings.Current.LlmEnabled;
        _llmService.ApiUrl = AppSettings.Current.LlmApiUrl;
        UpdateLayoutForMode();

        // Sync toggles
        _fullContextToggle.Checked = AppSettings.Current.LlmFullContextEnabled;
        _taggingToggle.Checked = AppSettings.Current.LlmTaggingEnabled;
        _searchToggle.Checked = AppSettings.Current.LlmSearchEnabled;
        _thinkingToggle.Checked = AppSettings.Current.LlmThinkingEnabled;
        _agentToggle.Checked = AppSettings.Current.LlmAgentModeEnabled;
    }

    public void FocusInput()
    {
        _allowExpandOnFocus = true;
        _inputBox.Focus();
    }

    private void BlockAutoCollapse(int milliseconds)
    {
        _autoCollapseBlockedUntilUtc = DateTime.UtcNow.AddMilliseconds(milliseconds);
    }

    private bool IsCursorOverInput()
    {
        try
        {
            var screenRect = _inputBox.RectangleToScreen(_inputBox.ClientRectangle);
            return screenRect.Contains(Cursor.Position);
        }
        catch
        {
            return false;
        }
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.SuppressKeyPress = true;
            SendPrompt();
        }
    }

    private void SendButton_Click(object? sender, EventArgs e)
    {
        SendPrompt();
    }

    private async void SendPrompt()
    {
        var prompt = _inputBox.Text.Trim();
        if (string.IsNullOrEmpty(prompt)) return;
        bool wasExpandedAtStart = _isExpanded;
        if (wasExpandedAtStart)
            BlockAutoCollapse(2000);

        var currentDir = GetCurrentDirectory?.Invoke();
        bool chatMode = AppSettings.Current.ChatModeEnabled;
        _llmService.ApiUrl = chatMode ? AppSettings.Current.LlmChatApiUrl : AppSettings.Current.LlmApiUrl;

        if (chatMode)
        {
            IWin32Window ownerWindow = (IWin32Window?)FindForm() ?? this;
            string? selectedModel = await _llmService.ResolveModelForTaskAsync(LlmUsageKind.Assistant, LlmTaskKind.Text, ownerWindow);
            if (string.IsNullOrWhiteSpace(selectedModel))
            {
                _statusLabel.Text = "Model selection cancelled";
                _statusLabel.ForeColor = Color.Orange;
                return;
            }

            // CHAT MODE
            _inputBox.Clear();
            AppendMessage("User", prompt, Color.SkyBlue);
            
            // Just add the prompt to history (without bulky context)
            _chatHistory.Add(new ChatMessage { Role = "user", Content = prompt });
            
            _statusLabel.Text = Localization.T("ai_thinking_status");
            _statusLabel.ForeColor = Color.Cyan;

            if (_agentToggle.Checked)
            {
                await HandleAgentModeChatAsync(prompt, currentDir, selectedModel, wasExpandedAtStart);
                _statusLabel.Text = Localization.T("ai_idle");
                _statusLabel.ForeColor = Color.Gray;
                if (wasExpandedAtStart)
                    BlockAutoCollapse(1200);
                return;
            }

            // Get current directory context for the system update
            string currentContext = "";
            if (!string.IsNullOrEmpty(currentDir) && currentDir != "::ThisPC")
            {
                currentContext = _fullContextToggle.Checked 
                    ? LlmPromptBuilder.BuildFullDirectoryContext(currentDir) 
                    : LlmPromptBuilder.BuildExtensionContext(currentDir);
            }

            var response = await _llmService.SendChatAsync(_chatHistory,
                _taggingToggle.Checked, _searchToggle.Checked, 
                _fullContextToggle.Checked, _thinkingToggle.Checked,
                currentContext, currentDir, selectedModel);
            
            _chatHistory.Add(new ChatMessage { Role = "assistant", Content = response });

            // Display reasoning tokens if available (from native API)
            if (!string.IsNullOrEmpty(_llmService.LastReasoning))
            {
                AppendMessage("AI (Reasoning Tokens)", _llmService.LastReasoning, Color.DimGray);
            }
            
            // Handle JSON response (mirrors command mode)
            try
            {
                var commands = LlmParsers.ParseCommands(response);
                
                // ALWAYS show the conversational response from AI
                AppendMessage("AI", LlmParsers.ExtractMessage(response), Color.LightGreen);

                if (commands.Count > 0)
                {
                    if (string.IsNullOrEmpty(currentDir) || currentDir == "::ThisPC")
                    {
                        AppendMessage("System", "[Error] Cannot execute commands in 'This PC' view.", Color.OrangeRed);
                    }
                    else
                    {
                        var ops = new LlmExecutor(currentDir, GetOwnerHandle?.Invoke() ?? IntPtr.Zero).ExecuteCommands(commands);
                        if (ops.Count > 0)
                        {
                            if (ops.Count == 1)
                                UndoRedoManager.Instance.RecordOperation(ops[0]);
                            else
                                UndoRedoManager.Instance.RecordOperation(new BatchOperation(ops, $"AI (Chat): {TruncatePrompt(prompt)}"));
                        }
                        AppendMessage("System", $"✅ Executed {ops.Count} commands.", Color.LightGreen);
                        if (wasExpandedAtStart)
                        {
                            BlockAutoCollapse(2000);
                            ExpandPanel();
                        }
                        OnOperationsComplete?.Invoke();
                    }
                }
            }
            catch
            {
                // Fallback: Just show the raw response if it's not valid/expected JSON
                AppendMessage("AI", response, Color.LightGreen);
            }
            
            _statusLabel.Text = Localization.T("ai_idle");
            _statusLabel.ForeColor = Color.Gray;
            if (wasExpandedAtStart)
                BlockAutoCollapse(1200);
            return;
        }

        // COMMAND MODE (Legacy)
        if (string.IsNullOrEmpty(currentDir) || currentDir == "::ThisPC")
        {
            _statusLabel.Text = "❌ Cannot use AI in This PC view";
            _statusLabel.ForeColor = Color.OrangeRed;
            return;
        }

        _inputBox.Enabled = false;
        _sendButton.Enabled = false;
        _statusLabel.Text = "Processing...";
        _statusLabel.ForeColor = Color.Cyan;

        try
        {
            IWin32Window ownerWindow = (IWin32Window?)FindForm() ?? this;
            string? selectedModel = await _llmService.ResolveModelForTaskAsync(LlmUsageKind.Assistant, LlmTaskKind.Text, ownerWindow);
            if (string.IsNullOrWhiteSpace(selectedModel))
            {
                _statusLabel.Text = "Model selection cancelled";
                _statusLabel.ForeColor = Color.Orange;
                return;
            }

            var files = new List<string>();
            try
            {
                 files = Directory.GetFileSystemEntries(currentDir).Select(Path.GetFileName).ToList()!;
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }

            string fileContext = string.Join(", ", files.Take(200)); 
            if (files.Count > 200) fileContext += $" ... and {files.Count - 200} more";

            string jsonResponse = await _llmService.SendPromptAsync(prompt, fileContext, 
                _fullContextToggle.Checked, _taggingToggle.Checked, 
                _searchToggle.Checked, _thinkingToggle.Checked, null, selectedModel);
                
            var commands = LlmParsers.ParseCommands(jsonResponse);

            if (commands.Count > 0)
            {
                var ops = new LlmExecutor(currentDir, GetOwnerHandle?.Invoke() ?? IntPtr.Zero).ExecuteCommands(commands);
                
                // Record for Undo
                if (ops.Count > 0)
                {
                    // Group operations into a batch if multiple, or record single
                    if (ops.Count == 1)
                        UndoRedoManager.Instance.RecordOperation(ops[0]);
                    else
                        UndoRedoManager.Instance.RecordOperation(new BatchOperation(ops, $"AI: {TruncatePrompt(prompt)}"));
                }

                _statusLabel.Text = $"✅ Executed {ops.Count} commands";
                _statusLabel.ForeColor = Color.LimeGreen;
                _inputBox.Clear();
                if (wasExpandedAtStart)
                {
                    BlockAutoCollapse(2000);
                    ExpandPanel();
                }
                
                OnOperationsComplete?.Invoke();
            }
            else
            {
                _statusLabel.Text = "ℹ️ No commands generated";
                _statusLabel.ForeColor = Color.Orange;
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"❌ {ex.Message}";
            _statusLabel.ForeColor = Color.OrangeRed;
            LlmDebugLogger.LogError(ex.ToString());
        }
        finally
        {
            _inputBox.Enabled = true;
            _sendButton.Enabled = true;
        }
    }

    private async Task HandleAgentModeChatAsync(string userPrompt, string? currentDir, string selectedModel, bool wasExpandedAtStart)
    {
        try
        {
            string currentContext = BuildCurrentContextSnapshot(currentDir);
            LlmAgentChatDecision decision;
            string? heuristicRunTask = TryBuildHeuristicAgentRunTask(userPrompt);
            if (!string.IsNullOrWhiteSpace(heuristicRunTask))
            {
                decision = new LlmAgentChatDecision
                {
                    Action = "start_agent_run",
                    Message = "Working on that now.",
                    RunTask = heuristicRunTask
                };
                _chatHistory.Add(new ChatMessage
                {
                    Role = "system",
                    Content = "[LOCAL_ROUTER]\nPromoted this request directly to agent run because it clearly requires file changes."
                });
            }
            else
            {
                decision = await _llmService.SendAgentChatDecisionAsync(
                    _chatHistory,
                    currentDir,
                    currentContext,
                    _taggingToggle.Checked,
                    _searchToggle.Checked,
                    forceReplyOnly: false,
                    modelOverride: selectedModel);
            }

            string action = (decision.Action ?? "reply").Trim().ToLowerInvariant();

            if (action == "quick_commands")
            {
                if (decision.Commands.Count == 0)
                {
                    AppendMessage("AI", decision.Message, Color.LightGreen);
                    _chatHistory.Add(new ChatMessage { Role = "assistant", Content = decision.Message });
                    return;
                }

                if (!HasUsableDirectory(currentDir))
                {
                    string blockedMsg = "I can't run file tools in 'This PC' view. Open a real folder and try again.";
                    AppendMessage("System", blockedMsg, Color.OrangeRed);
                    _chatHistory.Add(new ChatMessage { Role = "system", Content = "[TOOL_EXECUTION_BLOCKED]\n" + blockedMsg });

                    var blockedReply = await _llmService.SendAgentChatDecisionAsync(
                        _chatHistory,
                        currentDir,
                        currentContext,
                        _taggingToggle.Checked,
                        _searchToggle.Checked,
                        forceReplyOnly: true,
                        modelOverride: selectedModel);
                    AppendMessage("AI", blockedReply.Message, Color.LightGreen);
                    _chatHistory.Add(new ChatMessage { Role = "assistant", Content = blockedReply.Message });
                    return;
                }

                if (!string.IsNullOrWhiteSpace(decision.Message))
                {
                    AppendMessage("AI", decision.Message, Color.LightGreen);
                    _chatHistory.Add(new ChatMessage { Role = "assistant", Content = decision.Message });
                }

                _statusLabel.Text = "Running quick tools...";
                _statusLabel.ForeColor = Color.Cyan;

                var executor = new LlmExecutor(currentDir!, GetOwnerHandle?.Invoke() ?? IntPtr.Zero);
                var (feedback, ops) = executor.ExecuteAgenticCommands(decision.Commands);
                if (ops.Count > 0)
                {
                    if (ops.Count == 1)
                        UndoRedoManager.Instance.RecordOperation(ops[0]);
                    else
                        UndoRedoManager.Instance.RecordOperation(new BatchOperation(ops, $"AI (Quick): {TruncatePrompt(userPrompt)}"));
                    OnOperationsComplete?.Invoke();
                }

                string toolResultMessage = BuildQuickToolResultSystemMessage(decision.Commands, feedback, ops.Count);
                _chatHistory.Add(new ChatMessage { Role = "system", Content = toolResultMessage });
                AppendMessage("System", $"Quick tools executed: {decision.Commands.Count} command(s).", Color.LightGoldenrodYellow);

                currentContext = BuildCurrentContextSnapshot(currentDir);
                var finalDecision = await _llmService.SendAgentChatDecisionAsync(
                    _chatHistory,
                    currentDir,
                    currentContext,
                    _taggingToggle.Checked,
                    _searchToggle.Checked,
                    forceReplyOnly: true,
                    modelOverride: selectedModel);

                AppendMessage("AI", finalDecision.Message, Color.LightGreen);
                _chatHistory.Add(new ChatMessage { Role = "assistant", Content = finalDecision.Message });

                if (wasExpandedAtStart)
                {
                    BlockAutoCollapse(1800);
                    ExpandPanel();
                }
                return;
            }

            if (action == "start_agent_run")
            {
                if (!HasUsableDirectory(currentDir))
                {
                    string blockedMsg = "I can't start the agent loop in 'This PC' view. Open a real folder and try again.";
                    AppendMessage("System", blockedMsg, Color.OrangeRed);
                    _chatHistory.Add(new ChatMessage { Role = "system", Content = "[AGENT_RUN_BLOCKED]\n" + blockedMsg });

                    var blockedReply = await _llmService.SendAgentChatDecisionAsync(
                        _chatHistory,
                        currentDir,
                        currentContext,
                        _taggingToggle.Checked,
                        _searchToggle.Checked,
                        forceReplyOnly: true,
                        modelOverride: selectedModel);
                    AppendMessage("AI", blockedReply.Message, Color.LightGreen);
                    _chatHistory.Add(new ChatMessage { Role = "assistant", Content = blockedReply.Message });
                    return;
                }

                if (!string.IsNullOrWhiteSpace(decision.Message))
                {
                    AppendMessage("AI", decision.Message, Color.LightGreen);
                    _chatHistory.Add(new ChatMessage { Role = "assistant", Content = decision.Message });
                }

                string runTask = string.IsNullOrWhiteSpace(decision.RunTask) ? userPrompt : decision.RunTask.Trim();
                _statusLabel.Text = "Agent loop running...";
                _statusLabel.ForeColor = Color.Cyan;

                var progress = new Progress<string>(statusMsg =>
                {
                    AppendMessage("Agent", statusMsg, Color.LightGoldenrodYellow);
                });

                var loopHistory = new List<ChatMessage>
                {
                    new ChatMessage { Role = "user", Content = runTask }
                };

                var ops = await _llmService.RunAgenticTaskAsync(
                    loopHistory,
                    currentDir!,
                    _taggingToggle.Checked,
                    _searchToggle.Checked,
                    new LlmExecutor(currentDir!, GetOwnerHandle?.Invoke() ?? IntPtr.Zero),
                    progress,
                    selectedModel);

                if (ops.Count > 0)
                {
                    UndoRedoManager.Instance.RecordOperation(new BatchOperation(ops, $"AI (Agent): {TruncatePrompt(userPrompt)}"));
                    OnOperationsComplete?.Invoke();
                }

                var report = _llmService.LastAgentRunReport ?? new LlmAgentRunReport
                {
                    Request = runTask,
                    Model = selectedModel,
                    LoopsUsed = 0,
                    MaxLoops = AppSettings.Current.LlmAgentMaxLoops,
                    Completed = ops.Count > 0,
                    ClosureVerificationRan = false,
                    CommandsExecuted = 0,
                    ReadOnlyCommandsExecuted = 0,
                    WriteCommandsExecuted = 0,
                    UndoOperationRecords = ops.Count,
                    StopReason = "Run report unavailable.",
                    Events = new List<string>(),
                    ModelSummary = _llmService.LastAgentFinalResponse,
                    FinishedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                };

                _chatHistory.Add(new ChatMessage
                {
                    Role = "system",
                    Content = LlmService.BuildAgentRunReportSystemMessage(report)
                });
                _chatHistory.Add(new ChatMessage
                {
                    Role = "user",
                    Content = "The agent run is finished. Based only on the latest [AGENT_RUN_REPORT], provide the final result to the user now."
                });

                AppendMessage("System", $"Agent run complete: {report.StopReason}", report.Completed ? Color.LightGreen : Color.Goldenrod);

                currentContext = BuildCurrentContextSnapshot(currentDir);
                var postRunDecision = await _llmService.SendAgentChatDecisionAsync(
                    _chatHistory,
                    currentDir,
                    currentContext,
                    _taggingToggle.Checked,
                    _searchToggle.Checked,
                    forceReplyOnly: true,
                    modelOverride: selectedModel);

                string finalMessage = string.IsNullOrWhiteSpace(postRunDecision.Message)
                    ? (report.Completed ? "Task completed." : $"Task stopped: {report.StopReason}")
                    : postRunDecision.Message;
                if (LooksLikeFuturePlanReply(finalMessage))
                    finalMessage = BuildDeterministicPostRunReply(report);

                AppendMessage("AI", finalMessage, Color.LightGreen);
                _chatHistory.Add(new ChatMessage { Role = "assistant", Content = finalMessage });

                if (wasExpandedAtStart)
                {
                    BlockAutoCollapse(2200);
                    ExpandPanel();
                }
                return;
            }

            AppendMessage("AI", decision.Message, Color.LightGreen);
            _chatHistory.Add(new ChatMessage { Role = "assistant", Content = decision.Message });
        }
        catch (Exception ex)
        {
            AppendMessage("System", $"Error in Agent Mode: {ex.Message}", Color.OrangeRed);
            LlmDebugLogger.LogError($"Agent mode chat failed: {ex}");
        }
    }

    private static bool HasUsableDirectory(string? dir)
    {
        return !string.IsNullOrWhiteSpace(dir) && !string.Equals(dir, "::ThisPC", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildCurrentContextSnapshot(string? currentDir)
    {
        if (!HasUsableDirectory(currentDir))
            return "(no active directory context)";

        return _fullContextToggle.Checked
            ? LlmPromptBuilder.BuildFullDirectoryContext(currentDir!)
            : LlmPromptBuilder.BuildExtensionContext(currentDir!);
    }

    private static string BuildQuickToolResultSystemMessage(List<LlmCommand> commands, List<string> feedback, int opsCount)
    {
        var commandPayload = commands.Select(c => new
        {
            cmd = c.Cmd,
            path = c.Path,
            root = c.Root,
            pattern = c.Pattern,
            include_metadata = c.IncludeMetadata,
            tags = c.Tags
        }).ToList();

        var feedbackPayload = feedback
            .Select(TrimFeedbackForSystemMessage)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Take(12)
            .ToList();

        var payload = new
        {
            type = "quick_tool_result",
            commands = commandPayload,
            feedback = feedbackPayload,
            undo_operation_records = opsCount
        };

        return "[QUICK_TOOL_RESULT]\n" + JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string TrimFeedbackForSystemMessage(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";

        string normalized = input.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        const int maxLen = 2200;
        if (normalized.Length <= maxLen)
            return normalized;
        return normalized.Substring(0, maxLen) + "\n...(truncated)";
    }

    private static bool LooksLikeFuturePlanReply(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string t = text.Trim().ToLowerInvariant();
        if (t.StartsWith("i'll ") || t.StartsWith("i will ") || t.StartsWith("i’m going to ") || t.StartsWith("i am going to "))
            return true;

        return t.Contains("i'll organize") ||
               t.Contains("i will organize") ||
               t.Contains("i'll move") ||
               t.Contains("i will move");
    }

    private static string BuildDeterministicPostRunReply(LlmAgentRunReport report)
    {
        if (report.Completed)
        {
            if (report.UndoOperationRecords > 0)
            {
                return $"Task completed. Loops used: {report.LoopsUsed}/{report.MaxLoops}. " +
                       $"Applied changes with {report.UndoOperationRecords} undo record(s).";
            }

            return $"Task completed. Loops used: {report.LoopsUsed}/{report.MaxLoops}.";
        }

        string baseMsg = $"Task stopped before full completion. Stop reason: {report.StopReason}. " +
                         $"Loops used: {report.LoopsUsed}/{report.MaxLoops}.";
        if (report.UndoOperationRecords > 0)
            baseMsg += $" Some changes were applied ({report.UndoOperationRecords} undo record(s)).";
        return baseMsg;
    }

    private string TruncatePrompt(string prompt)
    {
        return prompt.Length > 30 ? prompt.Substring(0, 27) + "..." : prompt;
    }

    private int CountMoves(List<FileOperation> operations)
    {
        int count = 0;
        foreach (var op in operations)
        {
            if (op is MoveOperation move)
                count += move.SourcePaths.Count;
        }
        return count;
    }

    private static string? TryBuildHeuristicAgentRunTask(string? userPrompt)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
            return null;

        string text = userPrompt.Trim();
        string lower = text.ToLowerInvariant();

        string[] writeSignals =
        {
            "move ", "rename ", "organize ", "reorganize ", "sort ", "group ", "put ",
            "create folder", "make folder", "create file", "make file", "tag ", "retag ",
            "split ", "separate ", "clean up", "cleanup ", "arrange ", "place "
        };

        string[] inspectionSignals =
        {
            "what files", "which files", "show ", "list ", "find ", "search ", "how many ",
            "do i have", "is there", "what's in", "what is in", "check "
        };

        bool hasWriteSignal = writeSignals.Any(signal => lower.Contains(signal, StringComparison.Ordinal));
        bool hasInspectionSignal = inspectionSignals.Any(signal => lower.Contains(signal, StringComparison.Ordinal));
        bool mentionsDestination = lower.Contains(" into ") || lower.Contains(" to ") || lower.Contains(" folder");
        bool mentionsExtension = lower.Contains("*.") || lower.Contains(" .jpg") || lower.Contains(" .jpeg") ||
                                 lower.Contains(" .png") || lower.Contains(" .gif") || lower.Contains(" .webp") ||
                                 lower.Contains(" .bmp") || lower.Contains(" .txt") || lower.Contains(" .pdf") ||
                                 lower.Contains(" by extension");

        if (hasWriteSignal || (mentionsExtension && mentionsDestination && !hasInspectionSignal))
            return text;

        return null;
    }
}
