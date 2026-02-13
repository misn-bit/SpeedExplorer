using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class LayoutController
    {
        private readonly MainForm _owner;

        public LayoutController(MainForm owner)
        {
            _owner = owner;
        }

        public void InitializeLayoutAndLifecycle(string? normalizedStartup)
        {
            BuildMainLayout();
            WireStartupAndLifecycle(normalizedStartup);
            TrySetAppIcon();
            _owner.LoadFolderSettings();
            InitializeUiServices();
        }

        public Button CreateNavButton(string text, string tooltip)
        {
            var btn = new Button
            {
                Text = text,
                Size = new Size(_owner.Scale(36), _owner.Scale(31)),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = ForeColor_Dark,
                Font = new Font("Segoe UI", 12),
                Cursor = Cursors.Hand,
                Margin = _owner.Scale(new Padding(2, 0, 2, 0))
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);

            var tt = new ToolTip();
            tt.SetToolTip(btn, tooltip);

            return btn;
        }

        public void ApplyNavButtonTextOffset(Button btn, int offsetY)
        {
            string text = btn.Text;
            btn.Text = "";
            btn.Tag = text;
            btn.Paint += (s, e) =>
            {
                var b = (Button)s!;
                if (b.Tag is not string t)
                    return;
                var rect = b.ClientRectangle;
                rect.Offset(0, offsetY);
                var color = b.Enabled ? b.ForeColor : SystemColors.GrayText;
                TextRenderer.DrawText(e.Graphics, t, b.Font, rect, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            };
        }

        public Panel CreateSearchControl()
        {
            _owner._searchBox = new TextBox
            {
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.Gray,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10),
                Text = Localization.T("search_placeholder"),
                Dock = DockStyle.Fill
            };

            _owner._searchBox.GotFocus += (s, e) =>
            {
                if (_owner._searchBox.Text == Localization.T("search_placeholder"))
                {
                    _owner._searchBox.Text = "";
                    _owner._searchBox.ForeColor = ForeColor_Dark;
                }
            };

            _owner._searchBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_owner._searchBox.Text))
                {
                    _owner._searchBox.Text = Localization.T("search_placeholder");
                    _owner._searchBox.ForeColor = Color.Gray;
                    _owner._searchController.ClearSearch();
                }
            };

            _owner._searchBox.TextChanged += (s, e) =>
            {
                if (_owner._suppressSearchTextChanged || !_owner._searchBox.Enabled || _owner._isShellMode)
                    return;
                if (_owner._searchBox.Text != Localization.T("search_placeholder"))
                {
                    _owner._searchController.StartSearch(_owner._searchBox.Text);
                }
            };

            _owner._searchBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    if (!_owner._searchController.TryCancelActiveSearch())
                        _owner._searchBox.Text = "";
                    _owner._listView.Focus();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    _owner.ExecuteAction("FocusFilePanel");
                }
            };

            var tagToggleBtn = new Button
            {
                Text = "🏷️",
                Dock = DockStyle.Right,
                Width = _owner.Scale(26),
                Font = new Font("Segoe UI Emoji", 9f, FontStyle.Regular),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.Gray,
                Cursor = Cursors.Hand,
                Margin = new Padding(_owner.Scale(6), _owner.Scale(8), 0, 0)
            };
            tagToggleBtn.FlatAppearance.BorderSize = 0;
            tagToggleBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);
            ApplyNavButtonTextOffset(tagToggleBtn, -_owner.Scale(1));

            var tt = new ToolTip();
            tt.SetToolTip(tagToggleBtn, Localization.T("tooltip_search_tags"));

            tagToggleBtn.Click += (s, e) =>
            {
                bool enabled = _owner._searchController.ToggleTagOnly();
                tagToggleBtn.ForeColor = enabled ? AccentColor : Color.Gray;
                tagToggleBtn.BackColor = enabled ? Color.FromArgb(60, 60, 60) : Color.FromArgb(45, 45, 45);

                if (!string.IsNullOrWhiteSpace(_owner._searchBox.Text) &&
                    _owner._searchBox.Text != Localization.T("search_placeholder"))
                {
                    _owner._searchController.StartSearch(_owner._searchBox.Text);
                }
            };

            var clearBtn = new Button
            {
                Text = "✕",
                Dock = DockStyle.Right,
                Width = _owner.Scale(24),
                Font = new Font("Segoe UI Emoji", 10f, FontStyle.Regular),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.Gray,
                Cursor = Cursors.Hand,
                Margin = new Padding(0)
            };
            clearBtn.FlatAppearance.BorderSize = 0;
            clearBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);
            ApplyNavButtonTextOffset(clearBtn, -_owner.Scale(0));
            clearBtn.Click += (s, e) =>
            {
                _owner._searchBox.Text = "";
                _owner._searchBox.Focus();
            };

            var panel = new Panel
            {
                BackColor = Color.FromArgb(45, 45, 45),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = _owner.Scale(new Padding(2))
            };

            panel.Controls.Add(_owner._searchBox);
            panel.Controls.Add(tagToggleBtn);
            panel.Controls.Add(clearBtn);

            return panel;
        }

        public StatusStrip CreateStatusBar()
        {
            var status = new StatusStrip
            {
                BackColor = BackColor_Dark,
                ForeColor = ForeColor_Dark,
                SizingGrip = true
            };

            _owner._pathLabel = new ToolStripStatusLabel("")
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _owner._statusLabel = new ToolStripStatusLabel(Localization.T("status_loading"))
            {
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleRight,
                BorderSides = ToolStripStatusLabelBorderSides.Left,
                BorderStyle = Border3DStyle.Etched
            };

            _owner._viewToggleLabel = new ToolStripStatusLabel(Localization.T("view_tiles"))
            {
                AutoSize = true,
                IsLink = false
            };
            _owner._viewToggleLabel.Click += (s, e) => _owner.ToggleTileView();
            _owner._viewToggleLabel.ForeColor = Color.LightGray;
            _owner._viewToggleLabel.Margin = new Padding(10, 0, 10, 0);

            status.Items.Add(_owner._pathLabel);
            status.Items.Add(_owner._statusLabel);
            status.Items.Add(_owner._viewToggleLabel);

            return status;
        }

        private void BuildMainLayout()
        {
            _owner._titleBar.Dock = DockStyle.Top;

            _owner._navPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = _owner.Scale(47),
                BackColor = BackColor_Dark,
                Padding = _owner.Scale(new Padding(10, 5, 10, 5))
            };

            _owner._addressBar.Dock = DockStyle.Fill;

            _owner._navButtonsPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = _owner.Scale(200),
                BackColor = BackColor_Dark
            };

            _owner._backBtn = CreateNavButton("<", Localization.T("tooltip_back"));
            ApplyNavButtonTextOffset(_owner._backBtn, -_owner.Scale(4));
            _owner._backBtn.Click += (s, e) => _owner.GoBack();
            _owner._backBtn.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Middle || _owner._nav.BackHistory.Count == 0)
                    return;
                OpenPathWithMiddleClickPreference(_owner._nav.BackHistory.Peek());
            };

            _owner._fwdBtn = CreateNavButton(">", Localization.T("tooltip_forward"));
            ApplyNavButtonTextOffset(_owner._fwdBtn, -_owner.Scale(4));
            _owner._fwdBtn.Click += (s, e) => _owner.GoForward();
            _owner._fwdBtn.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Middle || _owner._nav.ForwardHistory.Count == 0)
                    return;
                OpenPathWithMiddleClickPreference(_owner._nav.ForwardHistory.Peek());
            };

            _owner._upBtn = CreateNavButton("^", Localization.T("tooltip_up"));
            ApplyNavButtonTextOffset(_owner._upBtn, -_owner.Scale(1));
            _owner._upBtn.Click += (s, e) => _owner.GoUp();
            _owner._upBtn.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Middle)
                    return;
                OpenPathWithMiddleClickPreference(GetUpTargetPath());
            };

            _owner._refreshBtn = CreateNavButton("R", Localization.T("tooltip_refresh"));
            ApplyNavButtonTextOffset(_owner._refreshBtn, -_owner.Scale(4));
            _owner._refreshBtn.Click += (s, e) => _ = _owner.RefreshCurrentAsync();

            _owner._settingsBtn = CreateNavButton("⚙", Localization.T("tooltip_settings"));
            ApplyNavButtonTextOffset(_owner._settingsBtn, -_owner.Scale(3));
            _owner._settingsBtn.Click += (s, e) => _owner.OpenSettings();

            var navFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                AutoScroll = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = BackColor_Dark,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            navFlow.Controls.AddRange(new Control[] { _owner._backBtn, _owner._fwdBtn, _owner._upBtn, _owner._refreshBtn, _owner._settingsBtn });
            _owner._navButtonsPanel.Controls.Add(navFlow);

            _owner._searchControl = CreateSearchControl();
            _owner._searchControl.Width = _owner.Scale(250);
            _owner._searchControl.Dock = DockStyle.Right;

            _owner._navPanel.Controls.Add(_owner._addressBar);
            _owner._navPanel.Controls.Add(_owner._navButtonsPanel);
            _owner._navPanel.Controls.Add(new Panel { Dock = DockStyle.Right, Width = _owner.Scale(10), BackColor = BackColor_Dark });
            _owner._navPanel.Controls.Add(_owner._searchControl);

            _owner.ApplySettings();

            _owner._splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterWidth = _owner.Scale(3),
                BackColor = BackColor_Dark,
                Panel1MinSize = _owner.Scale(120)
            };
            _owner._splitContainer.Panel1.BackColor = SidebarColor;
            _owner._splitContainer.Panel2.BackColor = ListBackColor;
            _owner._sidebar.Dock = DockStyle.Fill;
            _owner._listView.Dock = DockStyle.Fill;
            _owner._splitContainer.Panel1.Controls.Add(_owner._sidebar);
            _owner._splitContainer.Panel2.Controls.Add(_owner._listView);

            _owner._llmChatPanel = new LlmChatPanel
            {
                GetCurrentDirectory = () => _owner._currentPath,
                GetOwnerHandle = () => _owner.Handle,
                // Debounce AI-triggered refresh together with watcher events to avoid refresh storms.
                OnOperationsComplete = () => _owner.RequestWatcherRefresh()
            };
            void InvalidateListAfterChatLayoutChange()
            {
                if (_owner._listView == null || _owner._listView.IsDisposed)
                    return;
                _owner.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        if (_owner._listView != null && !_owner._listView.IsDisposed && _owner._listView.IsHandleCreated)
                        {
                            _owner._listView.Invalidate();
                            _owner._listView.Update();
                        }
                    }
                    catch { }
                }));
            }
            _owner._llmChatPanel.VisibleChanged += (s, e) => InvalidateListAfterChatLayoutChange();
            _owner._llmChatPanel.SizeChanged += (s, e) => InvalidateListAfterChatLayoutChange();
            _owner._splitContainer.Panel2.Controls.Add(_owner._llmChatPanel);

            _owner.Controls.Add(_owner._splitContainer);
            _owner.Controls.Add(_owner._navPanel);
            _owner.Controls.Add(_owner._titleBar);
            _owner.Controls.Add(_owner._statusBar);

            _owner._splitContainer.SplitterMoved += (s, e) =>
            {
                if (_owner._splitContainer.Panel1Collapsed)
                    return;
                AppSettings.Current.SidebarSplitDistance = _owner.Unscale(_owner._splitContainer.SplitterDistance);
                if (_owner._splitContainer.Width > 0)
                {
                    var minRatio = (double)_owner._splitContainer.Panel1MinSize / _owner._splitContainer.Width;
                    var rawRatio = (double)_owner._splitContainer.SplitterDistance / _owner._splitContainer.Width;
                    AppSettings.Current.SidebarSplitAtMinimum = _owner._splitContainer.SplitterDistance <= _owner._splitContainer.Panel1MinSize + 1;
                    AppSettings.Current.SidebarSplitRatio = Math.Max(minRatio, Math.Min(0.9, rawRatio));
                }
                AppSettings.Current.Save();
            };
        }

        private void WireStartupAndLifecycle(string? normalizedStartup)
        {
            _owner.Load += (s, e) => _owner.UpdateScale();

            _owner.Load += async (s, e) =>
            {
                _owner.SuspendLayout();
                try
                {
                    _owner.ApplySidebarSplit();

                    if (AppSettings.Current.MainWindowFullscreen)
                    {
                        _owner.MaximizedBounds = Rectangle.Empty;
                        _owner.WindowState = FormWindowState.Maximized;
                    }
                    else if (AppSettings.Current.MainWindowMaximized)
                    {
                        _owner.MaximizedBounds = Screen.FromControl(_owner).WorkingArea;
                        _owner.WindowState = FormWindowState.Maximized;
                    }
                }
                finally
                {
                    _owner.ResumeLayout(true);
                }

                try
                {
                    // Keep the ListView realized before first navigation bind.
                    // Startup already runs with form redraw disabled and opacity 0,
                    // so this does not introduce a visible skeleton frame.
                    if (_owner._listView != null && !_owner._listView.IsDisposed)
                    {
                        _owner._listView.Visible = true;
                        if (!_owner._listView.IsHandleCreated)
                            _owner._listView.CreateControl();
                    }

                    var startPath = normalizedStartup ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (_owner._fastStartup)
                    {
                        SendMessage(_owner.Handle, WM_SETREDRAW, 1, 0);
                        _owner.Opacity = 1;
                        if (_owner._listView != null && !_owner._listView.IsDisposed)
                            _owner._listView.Visible = true;
                    }
                    await _owner.NavigateTo(startPath, _owner._startupSelectPaths);
                    _owner.RefreshFrame();
                    _owner.StretchTagsColumn();

                    if (!_owner._fastStartup)
                        await Task.Delay(250);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Startup Load error: {ex.Message}");
                }
                finally
                {
                    bool canUpdate = !(_owner.IsDisposed || _owner.Disposing) && _owner.IsHandleCreated;
                    if (canUpdate)
                    {
                        SendMessage(_owner.Handle, WM_SETREDRAW, 1, 0);
                        _owner.Refresh();

                        if (!_owner._fastStartup)
                            _owner.Opacity = 1;
                        _owner._loadCompleted = true;
                        if (_owner._listView != null && !_owner._listView.IsDisposed)
                            _owner._listView.Visible = true;

                        // One extra post-show repaint pass helps with first-frame clipped-row artifacts.
                        _owner.BeginInvoke((Action)(() =>
                        {
                            try
                            {
                                if (_owner._listView != null && !_owner._listView.IsDisposed && _owner._listView.IsHandleCreated)
                                {
                                    _owner.StabilizeStartupVirtualViewport();
                                    _owner._listView.Invalidate();
                                    _owner._listView.Update();
                                }
                            }
                            catch { }
                        }));

                        // And a couple delayed passes after layout/paint settles.
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(80).ConfigureAwait(false);
                            try
                            {
                                _owner.BeginInvoke((Action)(() =>
                                {
                                    if (_owner._listView != null && !_owner._listView.IsDisposed && _owner._listView.IsHandleCreated)
                                    {
                                        _owner.StabilizeStartupVirtualViewport();
                                        _owner._listView.Invalidate();
                                        _owner._listView.Update();
                                    }
                                }));
                            }
                            catch { }

                            await Task.Delay(140).ConfigureAwait(false);
                            try
                            {
                                _owner.BeginInvoke((Action)(() =>
                                {
                                    if (_owner._listView != null && !_owner._listView.IsDisposed && _owner._listView.IsHandleCreated)
                                    {
                                        _owner._listView.Invalidate();
                                        _owner._listView.Update();
                                    }
                                }));
                            }
                            catch { }
                        });
                    }
                }
            };

            _owner.Activated += (s, e) => _owner.RefreshFrame();

            _owner.Resize += (s, e) =>
            {
                bool isFullscreen = _owner.WindowState == FormWindowState.Maximized && _owner.MaximizedBounds == Rectangle.Empty;
                _owner.Padding = isFullscreen ? _owner.Scale(new Padding(8)) : _owner.Scale(new Padding(2));
                _owner.Invalidate();
            };

            _owner.FormClosing += (s, e) =>
            {
                bool isMaximized = _owner.WindowState == FormWindowState.Maximized;
                bool isFullscreen = isMaximized && _owner.MaximizedBounds == Rectangle.Empty;

                AppSettings.Current.MainWindowMaximized = isMaximized && !isFullscreen;
                AppSettings.Current.MainWindowFullscreen = isFullscreen;
                AppSettings.Current.Save();

                _owner.SaveFolderSettings();
                _owner._loadCts?.Cancel();
                _owner._searchController.CancelActive();
                _owner._repaintTimer?.Stop();
                try { _owner._watcherController.Dispose(); } catch { }
                try { _owner._dragDropController.Dispose(); } catch { }
                try { _owner._quickLookController.Dispose(); } catch { }
                try { _owner._iconZoomController.Dispose(); } catch { }
                try { _owner._headerTailController.Dispose(); } catch { }
                try { _owner._iconLoadService?.Dispose(); } catch { }
            };
        }

        private void TrySetAppIcon()
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null)
                    _owner.Icon = icon;
            }
            catch { }
        }

        private void InitializeUiServices()
        {
            _owner._iconLoadService = new IconLoadService(
                _owner,
                _owner._smallIcons,
                _owner._largeIcons,
                requestRepaint: () => _owner._needsRepaint = true,
                iconApplied: key => _owner._tileViewController.HandleIconReady(key),
                shouldLoadLargeIcons: () => _owner.IsTileView);
            _owner._iconLoadService.Start();

            _owner._repaintTimer = new System.Windows.Forms.Timer { Interval = 33 };
            _owner._repaintTimer.Tick += (s, e) =>
            {
                if (_owner._needsRepaint)
                {
                    _owner._needsRepaint = false;
                    _owner._listView.Invalidate();
                }
            };
            _owner._repaintTimer.Start();

            _owner._retryLoadTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _owner._retryLoadTimer.Tick += (s, e) =>
            {
                _owner._retryLoadTimer.Stop();
                if (!_owner._retryLoadPending || string.IsNullOrEmpty(_owner._retryLoadPath))
                    return;

                bool isEmpty = _owner._items.Count == 0 && !_owner.IsDriveItemsOnly();

                if (_owner._currentPath != _owner._retryLoadPath && !isEmpty)
                {
                    _owner._retryLoadPending = false;
                    return;
                }

                if (_owner.IsDriveItemsOnly() || isEmpty)
                {
                    if (_owner._currentPath == _owner._retryLoadPath)
                    {
                        _owner._retryLoadPending = false;
                        if (_owner.IsHandleCreated && !_owner.IsDisposed)
                        {
                            _owner.BeginInvoke(new Action(() =>
                                _owner.ObserveTask(_owner.NavigateTo(_owner._retryLoadPath), "LayoutController.RetryLoad")));
                        }
                    }
                }
                else
                {
                    _owner._retryLoadPending = false;
                }
            };
        }

        private string? GetUpTargetPath()
        {
            if (string.IsNullOrWhiteSpace(_owner._currentPath) || _owner._currentPath == ThisPcPath)
                return null;

            if (IsShellPath(_owner._currentPath))
                return _owner.GetShellParentPath(_owner._currentPath) ?? ThisPcPath;

            var parent = Directory.GetParent(_owner._currentPath);
            return parent?.FullName ?? ThisPcPath;
        }

        private void OpenPathWithMiddleClickPreference(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (!IsShellPath(path) && path != ThisPcPath && !FileSystemService.IsAccessible(path))
            {
                _owner._statusLabel.Text = string.Format(Localization.T("status_access_denied"), path);
                return;
            }

            _owner._openTargetController.OpenPathByMiddleClickPreference(path, activateTab: false);
        }
    }
}
