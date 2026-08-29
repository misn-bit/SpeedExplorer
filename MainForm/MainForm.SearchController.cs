using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    BrowserState ISearchHost.BrowserState => State;
    ListView ISearchHost.FileListView => _listView;
    ToolStripStatusLabel ISearchHost.StatusLabel => _statusLabel;
    bool ISearchHost.IsDisposed => IsDisposed;
    bool ISearchHost.Disposing => Disposing;
    bool ISearchHost.IsHandleCreated => IsHandleCreated;
    void ISearchHost.BeginInvoke(Action action) => BeginInvoke(action);
    void ISearchHost.Invoke(Action action) => Invoke(action);
    void ISearchHost.SetupDriveColumns(ListView listView) => SetupDriveColumns(listView);
    void ISearchHost.SetupFileColumns(ListView listView) => SetupFileColumns(listView);
    void ISearchHost.UpdateActiveTabTitle() => UpdateActiveTabTitle();
    void ISearchHost.RefreshSearchOverlayVisibility() => RefreshSearchOverlayVisibility();
    void ISearchHost.ResetListViewportTopAsync(int preferredIndex, string reason)
        => ResetListViewportTopAsync(preferredIndex, reason);
    void ISearchHost.LogListViewState(string scope, string stage) => LogListViewState(scope, stage);
    void ISearchHost.InvalidateListItem(int index) => InvalidateListItem(index);

    private Font? _searchOverlayFont;
    private void UpdateSearchTagToggleButtonState()
    {
        if (_searchTagToggleBtn == null || _searchTagToggleBtn.IsDisposed)
            return;

        bool enabled = _searchController.IsTagSearchOnly;
        _searchTagToggleBtn.ForeColor = enabled ? AccentColor : Color.Gray;
        _searchTagToggleBtn.BackColor = enabled ? HoverBackColor : ControlBackColor;
    }

    private void FocusSearchBox(bool tagOnly)
    {
        _searchController.SetTagOnly(tagOnly);
        UpdateSearchTagToggleButtonState();
        _searchBox.Focus();
        _searchBox.SelectAll();

        if (!string.IsNullOrWhiteSpace(_searchBox.Text) &&
            _searchBox.Text != Localization.T("search_placeholder"))
        {
            _searchController.StartSearch(_searchBox.Text);
        }
    }


    private void InitializeSearchOverlay()
    {
        _searchingOverlay = new Label
        {
            Dock = DockStyle.Fill,
            Visible = false,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = ListBackColor,
            ForeColor = MutedForeColor
        };

        UpdateSearchOverlayTextAndStyle();
        _listView.Controls.Add(_searchingOverlay);
        _searchingOverlay.BringToFront();
    }

    private void UpdateSearchOverlayTextAndStyle()
    {
        if (_searchingOverlay == null || _searchingOverlay.IsDisposed || _listView == null)
            return;

        _searchingOverlay.Text = Localization.T("search_overlay_searching");
        _searchingOverlay.BackColor = ListBackColor;
        _searchingOverlay.ForeColor = MutedForeColor;

        float targetSize = Math.Max(18f, _listView.Font.Size * 2.1f);
        if (_searchOverlayFont == null ||
            Math.Abs(_searchOverlayFont.Size - targetSize) > 0.1f)
        {
            var oldFont = _searchOverlayFont;
            _searchOverlayFont = new Font("Segoe UI", targetSize, FontStyle.Bold, GraphicsUnit.Point);
            _searchingOverlay.Font = _searchOverlayFont;
            oldFont?.Dispose();
        }
    }

    private void RefreshSearchOverlayVisibility()
    {
        if (_searchingOverlay == null || _searchingOverlay.IsDisposed)
            return;

        bool show = _searchController.IsSearchMode &&
                    _searchController.IsSearchInProgress &&
                    State.Items.Count == 0;

        if (_searchingOverlay.Visible != show)
            _searchingOverlay.Visible = show;

        if (show)
            _searchingOverlay.BringToFront();
    }

    private sealed class SearchController
    {
        private readonly ISearchHost _owner;
        private BrowserState State => _owner.BrowserState;
        private readonly string[] _spinnerFrames = new[] { "|", "/", "-", "\\" };
        private const int LivePublishMinIntervalMs = 120;
        private const int LivePublishMinResultsDelta = 40;
        private System.Windows.Forms.Timer? _spinnerTimer;
        private string _searchStatusBase = "";
        private int _spinnerFrameIndex = 0;

        private CancellationTokenSource? _cts;
        private bool _userScrolledDuringSearch;

        public bool IsSearchMode { get; private set; }
        public bool IsSearchInProgress { get; private set; }
        public bool IsTagSearchOnly { get; private set; }
        private bool HasProgressRow => IsSearchMode && IsSearchInProgress && _owner.FileListView != null && _owner.FileListView.VirtualMode && State.Items.Count > 0;

        public SearchController(ISearchHost owner)
        {
            _owner = owner;
        }

        public void SetTagOnly(bool enabled) => IsTagSearchOnly = enabled;

        public bool ToggleTagOnly()
        {
            IsTagSearchOnly = !IsTagSearchOnly;
            return IsTagSearchOnly;
        }

        public void NotifyScrollInteraction()
        {
            if (IsSearchMode && IsSearchInProgress)
                _userScrolledDuringSearch = true;
        }

        public void CancelActive()
        {
            try { _cts?.Cancel(); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
        }

        public bool TryCancelActiveSearch()
        {
            if (!IsSearchMode || _cts == null) return false;
            try { _cts.Cancel(); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
            return true;
        }

        public void StartSearch(string query)
        {
            _ = PerformSearchAsync(query);
        }

        public void RestoreCachedSearchState(string query)
        {
            CancelActive();
            _cts = null;
            IsSearchMode = true;
            IsSearchInProgress = false;
            StopStatusSpinner();
            _owner.UpdateActiveTabTitle();

            if (_owner.FileListView != null && !_owner.FileListView.IsDisposed)
            {
                int target = State.Items.Count;
                if (_owner.FileListView.VirtualListSize != target)
                    _owner.FileListView.VirtualListSize = target;
                _owner.FileListView.Invalidate();
            }

                int scanned = Math.Max(State.Items.Count, State.AllItems.Count);
                _owner.StatusLabel.Text = string.Format(Localization.T("status_search_done"), State.Items.Count, scanned);
            _owner.RefreshSearchOverlayVisibility();
        }

        public bool TryBuildProgressVirtualItem(int index, out ListViewItem item)
        {
            item = null!;
            if (!HasProgressRow || index != State.Items.Count)
                return false;

            item = new ListViewItem($"{Localization.T("search_overlay_searching")} {_spinnerFrames[_spinnerFrameIndex]}")
            {
                Tag = SearchProgressRowTag
            };

            while (item.SubItems.Count < _owner.FileListView.Columns.Count)
                item.SubItems.Add("");

            return true;
        }

        public void ClearSearch()
        {
            CancelActive();
            _cts = null;
            IsSearchInProgress = false;
            StopStatusSpinner();

            // Always restore list from current folder snapshot even if search mode flag desynced.
            if (!IsSearchMode && State.Items.Count > 0)
            {
                _owner.RefreshSearchOverlayVisibility();
                return;
            }

            _owner.FileListView.VirtualListSize = 0;
            IsSearchMode = false;

            if (State.CurrentPath == ThisPcPath)
                _owner.SetupDriveColumns(_owner.FileListView);
            else
                _owner.SetupFileColumns(_owner.FileListView);

            State.Items = new List<FileItem>(State.AllItems);
            FileSystemService.SortItems(State.Items, State.SortColumn, State.SortDirection, State.TaggedFilesOnTop);

            _owner.FileListView.BeginUpdate();
            try
            {
                _owner.FileListView.SelectedIndices.Clear();
                _owner.FileListView.VirtualListSize = 0;
                _owner.FileListView.VirtualListSize = State.Items.Count;
            }
            finally
            {
                _owner.FileListView.EndUpdate();
            }

            // Force viewport reset and full repaint after cancelling search to avoid stale top-index artifacts.
            _owner.BeginInvoke((Action)(() =>
            {
                if (_owner.FileListView == null || _owner.FileListView.IsDisposed || !_owner.FileListView.IsHandleCreated)
                    return;

                try
                {
                    _owner.FileListView.SelectedIndices.Clear();
                    _owner.FileListView.VirtualListSize = State.Items.Count;
                    if (State.Items.Count > 0)
                    {
                        try { SendMessage(_owner.FileListView.Handle, 0x1013 /* LVM_ENSUREVISIBLE */, 0, 0); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
                        try { _owner.FileListView.EnsureVisible(0); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
                    }
                    _owner.FileListView.Invalidate();
                    _owner.FileListView.Update();
                }
                catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
            }));

            _owner.StatusLabel.Text = string.Format(Localization.T("status_ready_items"), State.Items.Count);
            _owner.RefreshSearchOverlayVisibility();
            _owner.UpdateActiveTabTitle();
        }

        public void ExitSearchModeOnNavigate()
        {
            CancelActive();
            _cts = null;
            IsSearchMode = false;
            IsSearchInProgress = false;
            StopStatusSpinner();
            _owner.RefreshSearchOverlayVisibility();
            _owner.UpdateActiveTabTitle();
        }

        private async Task PerformSearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                ClearSearch();
                return;
            }

            if (IsShellPath(State.CurrentPath))
            {
                _owner.StatusLabel.Text = Localization.T("search_not_supported");
                return;
            }

            CancelActive();
            var cts = new CancellationTokenSource();
            _cts = cts;
            _userScrolledDuringSearch = false;

            IsSearchMode = true;
            IsSearchInProgress = false;
            StopStatusSpinner();
            _owner.RefreshSearchOverlayVisibility();
            _owner.UpdateActiveTabTitle();
            _owner.LogListViewState("SEARCH", "start-before-reset");
            _owner.ResetListViewportTopAsync(0, "SEARCH-start");

            try { await Task.Delay(250, cts.Token); } catch { return; }

            // Ignore stale or inactive searches before touching UI/list state.
            if (!IsCurrentSearch(cts)) return;

            IsSearchInProgress = true;
            State.Items = new List<FileItem>();
            _owner.FileListView.VirtualListSize = 0;
            _owner.LogListViewState("SEARCH", "begin-empty-before-reset");
            _owner.ResetListViewportTopAsync(0, "SEARCH-empty");
            SetSearchStatus(Localization.T("status_searching_progress"), 0, 0);
            _owner.RefreshSearchOverlayVisibility();
            if (_owner.FileListView.Columns.Count == 0 ||
                (_owner.FileListView.Columns[0].Tag as ColumnMeta)?.Key != "col_name")
            {
                _owner.SetupFileColumns(_owner.FileListView);
            }

            List<FileItem> results = new List<FileItem>();
            int publishedCount = 0;
            int finalScanned = 0;
            int lastReportedScanned = -1;
            long lastReportTick = Environment.TickCount64;
            long lastLivePublishTick = 0;
            try
            {
                bool ShouldPublishStatus(int scanned)
                {
                    long now = Environment.TickCount64;
                    if (lastReportedScanned < 0 || scanned - lastReportedScanned >= 50 || now - lastReportTick >= 250)
                    {
                        lastReportedScanned = scanned;
                        lastReportTick = now;
                        return true;
                    }
                    return false;
                }

                void PublishLiveResults(bool force)
                {
                    if (_owner.FileListView == null || _owner.FileListView.IsDisposed)
                        return;

                    int availableCount = results.Count;
                    if (!force && availableCount == publishedCount)
                        return;

                    long now = Environment.TickCount64;
                    if (!force &&
                        publishedCount > 0 &&
                        availableCount - publishedCount < LivePublishMinResultsDelta &&
                        now - lastLivePublishTick < LivePublishMinIntervalMs)
                    {
                        return;
                    }

                    State.Items = results;
                    RefreshVirtualListSize();
                    if (publishedCount == 0 && availableCount > 0 && !_userScrolledDuringSearch)
                    {
                        _owner.LogListViewState("SEARCH", "first-batch-before-reset");
                        _owner.ResetListViewportTopAsync(0, "SEARCH-first-batch");
                    }

                    publishedCount = availableCount;
                    lastLivePublishTick = now;
                    _owner.FileListView.Invalidate();
                    _owner.RefreshSearchOverlayVisibility();
                }

                var uiUpdateAction = new Action<List<FileItem>>(foundBatch =>
                {
                    if (foundBatch == null || foundBatch.Count == 0 || cts.Token.IsCancellationRequested) return;

                    if (_owner.IsDisposed || _owner.Disposing || !_owner.IsHandleCreated)
                        return;

                    try
                    {
                        _owner.Invoke(new Action(() =>
                        {
                            if (IsCurrentSearch(cts))
                            {
                                results.AddRange(foundBatch);
                                PublishLiveResults(force: false);
                            }
                        }));
                    }
                    catch (InvalidOperationException)
                    {
                        // The form can lose its handle while a background search is
                        // publishing its final batch.
                    }
                });

                if (IsTagSearchOnly)
                {
                    SetSearchStatus(Localization.T("status_searching_tags"));
                    await FileSystemService.SearchTagsAsync(
                        State.CurrentPath,
                        query,
                        uiUpdateAction,
                        cts.Token);
                    finalScanned = results.Count;
                }
                else if (State.CurrentPath == ThisPcPath)
                {
                    SetSearchStatus(Localization.T("status_searching_all_drives"));
                    var drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name).ToList();
                    int totalSearched = 0;

                    foreach (var drive in drives)
                    {
                        if (cts.Token.IsCancellationRequested) break;
                        int driveSearched = 0;

                        var progress = new Progress<(int found, int searched)>(p =>
                        {
                            if (cts.Token.IsCancellationRequested) return;
                            driveSearched = p.searched;
                            int totalScanned = totalSearched + p.searched;
                            if (ShouldPublishStatus(totalScanned))
                                SetSearchStatus(Localization.T("status_searching_drive"), drive, results.Count, totalSearched + p.searched);
                        });

                        try
                        {
                            await FileSystemService.SearchFilesRecursiveAsync(drive, query, progress, uiUpdateAction, cts.Token);
                            totalSearched += driveSearched;
                            SetSearchStatus(Localization.T("status_searching_drive"), drive, results.Count, totalSearched);
                        }
                        catch (OperationCanceledException) { break; }
                        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
                    }
                    finalScanned = totalSearched;
                }
                else
                {
                    int pathSearched = 0;
                    var progress = new Progress<(int found, int searched)>(p =>
                    {
                        pathSearched = p.searched;
                        if (cts.Token.IsCancellationRequested) return;
                        if (ShouldPublishStatus(p.searched))
                            SetSearchStatus(Localization.T("status_searching_progress"), results.Count, p.searched);
                    });

                    await FileSystemService.SearchFilesRecursiveAsync(
                        State.CurrentPath,
                        query,
                        progress,
                        uiUpdateAction,
                        cts.Token);
                    finalScanned = pathSearched;
                }

                if (!IsCurrentSearch(cts)) return;

                PublishLiveResults(force: true);
                FileSystemService.SortItems(results, State.SortColumn, State.SortDirection, State.TaggedFilesOnTop);
                State.Items = results;
                IsSearchInProgress = false;

                _owner.FileListView.BeginUpdate();
                try
                {
                    _owner.FileListView.SelectedIndices.Clear();
                    _owner.FileListView.VirtualListSize = 0;
                    _owner.FileListView.VirtualListSize = State.Items.Count;
                    if (State.Items.Count > 0 && !_userScrolledDuringSearch)
                    {
                        try { _owner.FileListView.TopItem = _owner.FileListView.Items[0]; } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
                        try { _owner.FileListView.Items[0].EnsureVisible(); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
                    }
                }
                finally
                {
                    _owner.FileListView.EndUpdate();
                }

                StopStatusSpinner();
                finalScanned = Math.Max(finalScanned, State.Items.Count);
                _owner.StatusLabel.Text = string.Format(Localization.T("status_search_done"), State.Items.Count, finalScanned);
                _owner.LogListViewState("SEARCH", "done-before-reset");
                if (!_userScrolledDuringSearch)
                    _owner.ResetListViewportTopAsync(0, "SEARCH-done");
                _owner.RefreshSearchOverlayVisibility();
            }
            catch (OperationCanceledException)
            {
                if (IsCurrentSearch(cts))
                {
                    _owner.Invoke(() =>
                    {
                        if (IsCurrentSearch(cts))
                        {
                            FileSystemService.SortItems(results, State.SortColumn, State.SortDirection, State.TaggedFilesOnTop);
                            State.Items = results;
                            IsSearchInProgress = false;
                            _owner.FileListView.VirtualListSize = State.Items.Count;
                            StopStatusSpinner();
                            _owner.StatusLabel.Text = string.Format(Localization.T("status_search_stopped"), State.Items.Count);
                            _owner.LogListViewState("SEARCH", "stopped-before-reset");
                            if (!_userScrolledDuringSearch)
                                _owner.ResetListViewportTopAsync(0, "SEARCH-stopped");
                            _owner.FileListView.Invalidate();
                            _owner.RefreshSearchOverlayVisibility();
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                if (!IsCurrentSearch(cts)) return;
                StopStatusSpinner();
                _owner.StatusLabel.Text = string.Format(Localization.T("status_error"), ex.Message);
                IsSearchInProgress = false;
                _owner.RefreshSearchOverlayVisibility();
            }
        }

        private bool IsCurrentSearch(CancellationTokenSource cts)
        {
            return ReferenceEquals(cts, _cts) && IsSearchMode;
        }

        private void SetSearchStatus(string format, params object[] args)
        {
            _searchStatusBase = args.Length == 0 ? format : string.Format(format, args);
            EnsureStatusSpinnerRunning();
            _owner.StatusLabel.Text = $"{_searchStatusBase} {_spinnerFrames[_spinnerFrameIndex]}";
        }

        private void EnsureStatusSpinnerRunning()
        {
            if (_spinnerTimer == null)
            {
                _spinnerTimer = new System.Windows.Forms.Timer { Interval = 120 };
                _spinnerTimer.Tick += (s, e) =>
                {
                    if (!IsSearchInProgress || string.IsNullOrEmpty(_searchStatusBase))
                    {
                        _spinnerTimer?.Stop();
                        return;
                    }

                    _spinnerFrameIndex = (_spinnerFrameIndex + 1) % _spinnerFrames.Length;
                    _owner.StatusLabel.Text = $"{_searchStatusBase} {_spinnerFrames[_spinnerFrameIndex]}";
                    if (HasProgressRow)
                        _owner.InvalidateListItem(State.Items.Count);
                };
            }

            if (!_spinnerTimer.Enabled)
                _spinnerTimer.Start();
        }

        private void StopStatusSpinner()
        {
            _spinnerTimer?.Stop();
            _searchStatusBase = "";
            _spinnerFrameIndex = 0;
        }

        private void RefreshVirtualListSize()
        {
            if (_owner.FileListView == null || _owner.FileListView.IsDisposed || !_owner.FileListView.VirtualMode)
                return;

            int target = State.Items.Count + (HasProgressRow ? 1 : 0);
            if (_owner.FileListView.VirtualListSize != target)
                _owner.FileListView.VirtualListSize = target;
        }
    }
}
