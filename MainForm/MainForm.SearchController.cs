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
    string ISearchHost.ActiveTabId => _tabsController.ActiveTabId;
    ListView ISearchHost.FileListView => _listView;
    string ISearchHost.SearchText => _searchBox?.Text ?? "";
    ToolStripStatusLabel ISearchHost.StatusLabel => _statusLabel;
    ToolStripStatusLabel ISearchHost.SearchSpinnerLabel => _searchSpinnerLabel;
    bool ISearchHost.IsDisposed => IsDisposed;
    bool ISearchHost.Disposing => Disposing;
    bool ISearchHost.IsHandleCreated => IsHandleCreated;
    void ISearchHost.BeginInvoke(Action action) => BeginInvoke(action);
    void ISearchHost.Invoke(Action action) => Invoke(action);
    void ISearchHost.SetupDriveColumns(ListView listView) => SetupDriveColumns(listView);
    void ISearchHost.SetupFileColumns(ListView listView) => SetupFileColumns(listView);
    void ISearchHost.InvalidatePendingSearchRestore() => _tabsController.InvalidatePendingSearchRestore();
    void ISearchHost.UpdateActiveTabTitle() => UpdateActiveTabTitle();
    void ISearchHost.RefreshTabTitle(string tabId) => _tabsController.RefreshTabTitle(tabId);
    void ISearchHost.ResetListViewportTopAsync(int preferredIndex, string reason)
        => ResetListViewportTopAsync(preferredIndex, reason);
    void ISearchHost.LogListViewState(string scope, string stage) => LogListViewState(scope, stage);
    void ISearchHost.InvalidateListItem(int index) => InvalidateListItem(index);

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


    private sealed class SearchController
    {
        private readonly ISearchHost _owner;
        private BrowserState State => _owner.BrowserState;
        private readonly string[] _spinnerFrames = new[] { "│", "╱", "─", "╲" };
        private const int LivePublishMinIntervalMs = 120;
        private const int LivePublishMinResultsDelta = 40;
        private System.Windows.Forms.Timer? _spinnerTimer;
        private int _spinnerFrameIndex = 0;

        private sealed class SearchRun
        {
            public required string TabId { get; init; }
            public required string Path { get; init; }
            public required string Query { get; init; }
            public required List<FileItem> Results { get; init; }
            public CancellationTokenSource? Cancellation { get; set; }
            public bool IsInProgress { get; set; }
            public bool IsTagOnly { get; init; }
            public bool UserScrolled { get; set; }
            public bool WasStopped { get; set; }
            public int Scanned { get; set; }
            public string StatusText { get; set; } = "";
            public SortColumn SortColumn { get; init; }
            public SortDirection SortDirection { get; init; }
            public bool TaggedFilesOnTop { get; init; }
        }

        private readonly Dictionary<string, SearchRun> _runsByTab = new(StringComparer.Ordinal);
        private SearchRun? _activeRun;
        private System.Windows.Forms.Timer? _debounceTimer;
        private string _debounceQuery = "";
        private long _debounceGeneration;
        private long _scheduledDebounceGeneration;

        public bool IsSearchMode => _activeRun != null && IsActiveRun(_activeRun);
        public bool IsSearchInProgress => IsSearchMode && _activeRun!.IsInProgress;
        public bool IsTagSearchOnly { get; private set; }
        private bool HasProgressRow => IsSearchActiveForPath(State.CurrentPath) && IsSearchInProgress && _owner.FileListView != null && _owner.FileListView.VirtualMode;

        public bool IsSearchActiveForPath(string path)
            => IsSearchMode && string.Equals(_activeRun!.Path, path, StringComparison.OrdinalIgnoreCase);

        public bool IsTabSearchInProgress(string tabId)
            => _runsByTab.TryGetValue(tabId, out var run) && run.IsInProgress;

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
                _activeRun!.UserScrolled = true;
        }

        public void CancelActive()
        {
            _debounceTimer?.Stop();
            _debounceGeneration++;
            CancelRun(_activeRun);
        }

        public void CancelAllSearches()
        {
            _debounceTimer?.Stop();
            _debounceGeneration++;
            foreach (var run in _runsByTab.Values)
                CancelRun(run);
            _runsByTab.Clear();
            _activeRun = null;
            StopStatusSpinner();
        }

        public bool TryCancelActiveSearch()
        {
            if (!IsSearchMode || _activeRun!.Cancellation == null) return false;
            CancelActive();
            return true;
        }

        public void DetachForTabSwitch()
        {
            _debounceTimer?.Stop();
            _debounceGeneration++;
            _activeRun = null;
            StopStatusSpinner();
        }

        public void CloseTabSearch(string tabId)
        {
            if (_runsByTab.Remove(tabId, out var run))
                CancelRun(run);
            if (_activeRun?.TabId == tabId)
            {
                _activeRun = null;
                StopStatusSpinner();
            }
        }

        private static void CancelRun(SearchRun? run)
        {
            try { run?.Cancellation?.Cancel(); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
        }

        public void StartSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                if (string.IsNullOrWhiteSpace(_owner.SearchText))
                    ClearSearch();
                return;
            }

            if (!IsSearchInputCurrent(query))
                return;

            _debounceTimer?.Stop();
            _debounceGeneration++;
            string tabId = _owner.ActiveTabId;
            if (_runsByTab.TryGetValue(tabId, out var existing) &&
                string.Equals(existing.Path, State.CurrentPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Query, query, StringComparison.Ordinal) &&
                existing.IsTagOnly == IsTagSearchOnly)
            {
                ActivateRun(existing);
                return;
            }
            _ = PerformSearchAsync(query);
        }

        public void StartSearchDebounced(string query)
        {
            if (_debounceTimer == null)
            {
                _debounceTimer = new System.Windows.Forms.Timer { Interval = 400 };
                _debounceTimer.Tick += (s, e) =>
                {
                    _debounceTimer.Stop();
                    if (_scheduledDebounceGeneration != _debounceGeneration)
                        return;
                    StartSearch(_debounceQuery);
                };
            }
            _debounceQuery = query;
            _scheduledDebounceGeneration = ++_debounceGeneration;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        public void RestoreCachedSearchState(string query)
        {
            if (!IsSearchInputCurrent(query))
                return;

            if (_runsByTab.TryGetValue(_owner.ActiveTabId, out var existing) &&
                string.Equals(existing.Path, State.CurrentPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Query, query, StringComparison.Ordinal))
            {
                ActivateRun(existing);
                return;
            }

            if (_runsByTab.Remove(_owner.ActiveTabId, out existing))
                CancelRun(existing);

            var restored = new SearchRun
            {
                TabId = _owner.ActiveTabId,
                Path = State.CurrentPath,
                Query = query,
                Results = State.Items,
                IsTagOnly = IsTagSearchOnly,
                Scanned = Math.Max(State.Items.Count, State.AllItems.Count),
                SortColumn = State.SortColumn,
                SortDirection = State.SortDirection,
                TaggedFilesOnTop = State.TaggedFilesOnTop
            };
            _runsByTab[restored.TabId] = restored;
            ActivateRun(restored);
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

        public void RefreshProgressRow()
        {
            RefreshVirtualListSize();
            if (_owner.FileListView != null && !_owner.FileListView.IsDisposed)
                _owner.FileListView.Invalidate();
        }

        public void ClearSearch()
        {
            _debounceTimer?.Stop();
            _debounceGeneration++;
            bool wasSearchMode = IsSearchMode;
            if (_activeRun != null)
            {
                CancelRun(_activeRun);
                _runsByTab.Remove(_activeRun.TabId);
            }
            _activeRun = null;
            StopStatusSpinner();
            _owner.InvalidatePendingSearchRestore();

            // Always restore list from current folder snapshot even if search mode flag desynced.
            if (!wasSearchMode && State.Items.Count > 0)
            {
                RefreshProgressRow();
                _owner.UpdateActiveTabTitle();
                return;
            }

            _owner.FileListView.VirtualListSize = 0;

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
            RefreshProgressRow();
            _owner.UpdateActiveTabTitle();
        }

        public void ExitSearchModeOnNavigate()
        {
            _debounceTimer?.Stop();
            _debounceGeneration++;
            if (_activeRun != null)
            {
                CancelRun(_activeRun);
                _runsByTab.Remove(_activeRun.TabId);
            }
            _activeRun = null;
            StopStatusSpinner();
            RefreshProgressRow();
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

            string tabId = _owner.ActiveTabId;
            string searchPath = State.CurrentPath;
            if (_runsByTab.TryGetValue(tabId, out var previous))
            {
                CancelRun(previous);
                _runsByTab.Remove(tabId);
            }
            var cts = new CancellationTokenSource();
            var run = new SearchRun
            {
                TabId = tabId,
                Path = searchPath,
                Query = query,
                Results = new List<FileItem>(),
                Cancellation = cts,
                IsTagOnly = IsTagSearchOnly,
                SortColumn = State.SortColumn,
                SortDirection = State.SortDirection,
                TaggedFilesOnTop = State.TaggedFilesOnTop
            };
            _runsByTab[tabId] = run;
            _activeRun = run;
            StopStatusSpinner();
            RefreshProgressRow();
            _owner.UpdateActiveTabTitle();
            _owner.LogListViewState("SEARCH", "start-before-reset");
            _owner.ResetListViewportTopAsync(0, "SEARCH-start");

            try { await Task.Delay(250, cts.Token); }
            catch (OperationCanceledException)
            {
                run.Cancellation = null;
                run.WasStopped = true;
                run.StatusText = string.Format(Localization.T("status_search_stopped"), run.Results.Count);
                cts.Dispose();
                if (IsCurrentSearch(run) && IsActiveRun(run))
                {
                    StopStatusSpinner();
                    _owner.StatusLabel.Text = run.StatusText;
                }
                return;
            }

            if (!IsCurrentSearch(run)) { cts.Dispose(); run.Cancellation = null; return; }

            run.IsInProgress = true;
            _owner.RefreshTabTitle(run.TabId);
            if (IsActiveRun(run))
            {
                State.Items = run.Results;
                RefreshVirtualListSize();
                _owner.LogListViewState("SEARCH", "begin-empty-before-reset");
                _owner.ResetListViewportTopAsync(0, "SEARCH-empty");
                SetSearchStatus(run, Localization.T("status_searching_progress"), 0, 0);
                RefreshProgressRow();
            }
            if (IsActiveRun(run) && (_owner.FileListView.Columns.Count == 0 ||
                (_owner.FileListView.Columns[0].Tag as ColumnMeta)?.Key != "col_name")
                )
            {
                _owner.SetupFileColumns(_owner.FileListView);
            }

            List<FileItem> results = run.Results;
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
                    if (!IsActiveRun(run) || _owner.FileListView == null || _owner.FileListView.IsDisposed)
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
                    if (publishedCount == 0 && availableCount > 0 && !run.UserScrolled)
                    {
                        _owner.LogListViewState("SEARCH", "first-batch-before-reset");
                        _owner.ResetListViewportTopAsync(0, "SEARCH-first-batch");
                    }

                    publishedCount = availableCount;
                    lastLivePublishTick = now;
                    _owner.FileListView.Invalidate();
                    RefreshProgressRow();
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
                            if (IsCurrentSearch(run))
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

                if (run.IsTagOnly)
                {
                    SetSearchStatus(run, Localization.T("status_searching_tags"));
                    await FileSystemService.SearchTagsAsync(
                        run.Path,
                        query,
                        uiUpdateAction,
                        cts.Token);
                    finalScanned = results.Count;
                }
                else if (run.Path == ThisPcPath)
                {
                    SetSearchStatus(run, Localization.T("status_searching_all_drives"));
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
                                SetSearchStatus(run, Localization.T("status_searching_drive"), drive, results.Count, totalSearched + p.searched);
                        });

                        try
                        {
                            await FileSystemService.SearchFilesRecursiveAsync(drive, query, progress, uiUpdateAction, cts.Token);
                            totalSearched += driveSearched;
                            SetSearchStatus(run, Localization.T("status_searching_drive"), drive, results.Count, totalSearched);
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
                            SetSearchStatus(run, Localization.T("status_searching_progress"), results.Count, p.searched);
                    });

                    await FileSystemService.SearchFilesRecursiveAsync(
                        run.Path,
                        query,
                        progress,
                        uiUpdateAction,
                        cts.Token);
                    finalScanned = pathSearched;
                }

                if (!IsCurrentSearch(run)) return;

                FileSystemService.SortItems(results, run.SortColumn, run.SortDirection, run.TaggedFilesOnTop);
                run.IsInProgress = false;
                run.Scanned = Math.Max(finalScanned, results.Count);
                run.StatusText = string.Format(Localization.T("status_search_done"), results.Count, run.Scanned);
                run.Cancellation = null;
                cts.Dispose();
                if (IsActiveRun(run))
                {
                    State.Items = results;
                    _owner.FileListView.BeginUpdate();
                    try
                    {
                        _owner.FileListView.SelectedIndices.Clear();
                        _owner.FileListView.VirtualListSize = 0;
                        _owner.FileListView.VirtualListSize = State.Items.Count;
                        if (State.Items.Count > 0 && !run.UserScrolled)
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
                    _owner.StatusLabel.Text = run.StatusText;
                    _owner.LogListViewState("SEARCH", "done-before-reset");
                    if (!run.UserScrolled)
                        _owner.ResetListViewportTopAsync(0, "SEARCH-done");
                    RefreshProgressRow();
                }
                _owner.RefreshTabTitle(run.TabId);
            }
            catch (OperationCanceledException)
            {
                if (IsCurrentSearch(run))
                {
                    _owner.Invoke(() =>
                    {
                        if (IsCurrentSearch(run))
                        {
                            FileSystemService.SortItems(results, run.SortColumn, run.SortDirection, run.TaggedFilesOnTop);
                            run.IsInProgress = false;
                            run.WasStopped = true;
                            run.Scanned = Math.Max(run.Scanned, results.Count);
                            run.StatusText = string.Format(Localization.T("status_search_stopped"), results.Count);
                            run.Cancellation = null;
                            cts.Dispose();
                            if (IsActiveRun(run))
                            {
                                State.Items = results;
                                _owner.FileListView.VirtualListSize = State.Items.Count;
                                StopStatusSpinner();
                                _owner.StatusLabel.Text = run.StatusText;
                                _owner.LogListViewState("SEARCH", "stopped-before-reset");
                                if (!run.UserScrolled)
                                    _owner.ResetListViewportTopAsync(0, "SEARCH-stopped");
                                _owner.FileListView.Invalidate();
                                RefreshProgressRow();
                            }
                            _owner.RefreshTabTitle(run.TabId);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                if (!IsCurrentSearch(run)) return;
                run.IsInProgress = false;
                run.StatusText = string.Format(Localization.T("status_error"), ex.Message);
                run.Cancellation = null;
                cts.Dispose();
                if (IsActiveRun(run))
                {
                    StopStatusSpinner();
                    _owner.StatusLabel.Text = run.StatusText;
                    RefreshProgressRow();
                }
                _owner.RefreshTabTitle(run.TabId);
            }
            finally
            {
                if (run.Cancellation != null)
                {
                    run.Cancellation.Dispose();
                    run.Cancellation = null;
                }
            }
        }

        private bool IsCurrentSearch(SearchRun run)
        {
            return _runsByTab.TryGetValue(run.TabId, out var current) && ReferenceEquals(current, run);
        }

        private bool IsActiveRun(SearchRun run)
            => ReferenceEquals(_activeRun, run) &&
               string.Equals(_owner.ActiveTabId, run.TabId, StringComparison.Ordinal) &&
               string.Equals(State.CurrentPath, run.Path, StringComparison.OrdinalIgnoreCase);

        private void ActivateRun(SearchRun run)
        {
            _activeRun = run;
            State.Items = run.Results;
            if (_owner.FileListView != null && !_owner.FileListView.IsDisposed)
            {
                if (_owner.FileListView.Columns.Count == 0 ||
                    (_owner.FileListView.Columns[0].Tag as ColumnMeta)?.Key != "col_name")
                    _owner.SetupFileColumns(_owner.FileListView);
                _owner.FileListView.VirtualListSize = 0;
                _owner.FileListView.VirtualListSize = State.Items.Count + (run.IsInProgress ? 1 : 0);
                _owner.FileListView.Invalidate();
            }

            if (run.IsInProgress)
            {
                SetSearchStatus(run, string.IsNullOrEmpty(run.StatusText)
                    ? Localization.T("status_searching_progress")
                    : run.StatusText);
            }
            else if (run.Cancellation != null)
            {
                _owner.StatusLabel.Text = string.IsNullOrEmpty(run.StatusText)
                    ? Localization.T("status_searching_progress")
                    : run.StatusText;
            }
            else
            {
                StopStatusSpinner();
                if (!string.IsNullOrEmpty(run.StatusText))
                    _owner.StatusLabel.Text = run.StatusText;
                else
                    _owner.StatusLabel.Text = string.Format(Localization.T("status_search_done"), run.Results.Count, Math.Max(run.Scanned, run.Results.Count));
            }

            RefreshProgressRow();
            _owner.UpdateActiveTabTitle();
        }

        private bool IsSearchInputCurrent(string? query)
        {
            return !string.IsNullOrWhiteSpace(query) &&
                   !string.Equals(query, Localization.T("search_placeholder"), StringComparison.Ordinal) &&
                   string.Equals(query, _owner.SearchText, StringComparison.Ordinal);
        }

        private void SetSearchStatus(SearchRun run, string format, params object[] args)
        {
            run.StatusText = args.Length == 0 ? format : string.Format(format, args);
            if (!IsActiveRun(run))
                return;

            _owner.StatusLabel.Text = run.StatusText;
            _owner.SearchSpinnerLabel.Text = _spinnerFrames[_spinnerFrameIndex];
            EnsureStatusSpinnerRunning();
        }

        private void EnsureStatusSpinnerRunning()
        {
            if (_spinnerTimer == null)
            {
                _spinnerTimer = new System.Windows.Forms.Timer { Interval = 120 };
                _spinnerTimer.Tick += (s, e) =>
                {
                    if (!IsSearchInProgress || _activeRun == null || string.IsNullOrEmpty(_activeRun.StatusText))
                    {
                        _spinnerTimer?.Stop();
                        return;
                    }

                    _spinnerFrameIndex = (_spinnerFrameIndex + 1) % _spinnerFrames.Length;
                    _owner.SearchSpinnerLabel.Text = _spinnerFrames[_spinnerFrameIndex];
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
            _spinnerFrameIndex = 0;
            _owner.SearchSpinnerLabel.Text = "";
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
