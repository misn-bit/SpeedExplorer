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
    private void InitializeSearchOverlay()
    {
        _searchingOverlay = new Label
        {
            Dock = DockStyle.Fill,
            Visible = false,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = ListBackColor,
            ForeColor = Color.FromArgb(190, 190, 190)
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
        _searchingOverlay.ForeColor = Color.FromArgb(190, 190, 190);

        float targetSize = Math.Max(18f, _listView.Font.Size * 2.1f);
        if (_searchingOverlay.Font == null ||
            Math.Abs(_searchingOverlay.Font.Size - targetSize) > 0.1f ||
            _searchingOverlay.Font.Style != FontStyle.Bold)
        {
            _searchingOverlay.Font = new Font("Segoe UI", targetSize, FontStyle.Bold, GraphicsUnit.Point);
        }
    }

    private void RefreshSearchOverlayVisibility()
    {
        if (_searchingOverlay == null || _searchingOverlay.IsDisposed)
            return;

        bool show = _searchController.IsSearchMode &&
                    _searchController.IsSearchInProgress &&
                    _items.Count == 0;

        if (_searchingOverlay.Visible != show)
            _searchingOverlay.Visible = show;

        if (show)
            _searchingOverlay.BringToFront();
    }

    private sealed class SearchController
    {
        private readonly MainForm _owner;
        private readonly string[] _spinnerFrames = new[] { "|", "/", "-", "\\" };
        private System.Windows.Forms.Timer? _spinnerTimer;
        private string _searchStatusBase = "";
        private int _spinnerFrameIndex = 0;

        private CancellationTokenSource? _cts;

        public bool IsSearchMode { get; private set; }
        public bool IsSearchInProgress { get; private set; }
        public bool IsTagSearchOnly { get; private set; }
        private bool HasProgressRow => IsSearchMode && IsSearchInProgress && _owner._listView != null && _owner._listView.VirtualMode && _owner._items.Count > 0;

        public SearchController(MainForm owner)
        {
            _owner = owner;
        }

        public void SetTagOnly(bool enabled) => IsTagSearchOnly = enabled;

        public bool ToggleTagOnly()
        {
            IsTagSearchOnly = !IsTagSearchOnly;
            return IsTagSearchOnly;
        }

        public void CancelActive()
        {
            try { _cts?.Cancel(); } catch { }
        }

        public bool TryCancelActiveSearch()
        {
            if (!IsSearchMode || _cts == null) return false;
            try { _cts.Cancel(); } catch { }
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

            if (_owner._listView != null && !_owner._listView.IsDisposed)
            {
                int target = _owner._items.Count;
                if (_owner._listView.VirtualListSize != target)
                    _owner._listView.VirtualListSize = target;
                _owner._listView.Invalidate();
            }

            int scanned = Math.Max(_owner._items.Count, _owner._allItems.Count);
            _owner._statusLabel.Text = string.Format(Localization.T("status_search_done"), _owner._items.Count, scanned);
            _owner.RefreshSearchOverlayVisibility();
        }

        public bool TryBuildProgressVirtualItem(int index, out ListViewItem item)
        {
            item = null!;
            if (!HasProgressRow || index != _owner._items.Count)
                return false;

            item = new ListViewItem($"{Localization.T("search_overlay_searching")} {_spinnerFrames[_spinnerFrameIndex]}")
            {
                Tag = SearchProgressRowTag
            };

            while (item.SubItems.Count < _owner._listView.Columns.Count)
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
            if (!IsSearchMode && _owner._items.Count > 0)
            {
                _owner.RefreshSearchOverlayVisibility();
                return;
            }

            _owner._listView.VirtualListSize = 0;
            IsSearchMode = false;

            if (_owner._currentPath == ThisPcPath)
                _owner.SetupDriveColumns(_owner._listView);
            else
                _owner.SetupFileColumns(_owner._listView);

            _owner._items = new List<FileItem>(_owner._allItems);
            FileSystemService.SortItems(_owner._items, _owner._sortColumn, _owner._sortDirection);

            _owner._listView.BeginUpdate();
            try
            {
                _owner._listView.SelectedIndices.Clear();
                _owner._listView.VirtualListSize = 0;
                _owner._listView.VirtualListSize = _owner._items.Count;
            }
            finally
            {
                _owner._listView.EndUpdate();
            }

            // Force viewport reset and full repaint after cancelling search to avoid stale top-index artifacts.
            _owner.BeginInvoke((Action)(() =>
            {
                if (_owner._listView == null || _owner._listView.IsDisposed || !_owner._listView.IsHandleCreated)
                    return;

                try
                {
                    _owner._listView.SelectedIndices.Clear();
                    _owner._listView.VirtualListSize = _owner._items.Count;
                    if (_owner._items.Count > 0)
                    {
                        try { SendMessage(_owner._listView.Handle, 0x1013 /* LVM_ENSUREVISIBLE */, 0, 0); } catch { }
                        try { _owner._listView.EnsureVisible(0); } catch { }
                    }
                    _owner._listView.Invalidate();
                    _owner._listView.Update();
                }
                catch { }
            }));

            _owner._statusLabel.Text = string.Format(Localization.T("status_ready_items"), _owner._items.Count);
            _owner.RefreshSearchOverlayVisibility();
        }

        public void ExitSearchModeOnNavigate()
        {
            CancelActive();
            _cts = null;
            IsSearchMode = false;
            IsSearchInProgress = false;
            StopStatusSpinner();
            _owner.RefreshSearchOverlayVisibility();
        }

        private async Task PerformSearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                ClearSearch();
                return;
            }

            if (IsShellPath(_owner._currentPath))
            {
                _owner._statusLabel.Text = Localization.T("search_not_supported");
                return;
            }

            CancelActive();
            var cts = new CancellationTokenSource();
            _cts = cts;

            IsSearchMode = true;
            IsSearchInProgress = false;
            StopStatusSpinner();
            _owner.RefreshSearchOverlayVisibility();
            _owner.LogListViewState("SEARCH", "start-before-reset");
            _owner.ResetListViewportTopAsync(0, "SEARCH-start");

            try { await Task.Delay(250, cts.Token); } catch { return; }

            // Ignore stale or inactive searches before touching UI/list state.
            if (!IsCurrentSearch(cts)) return;

            IsSearchInProgress = true;
            _owner._items = new List<FileItem>();
            _owner._listView.VirtualListSize = 0;
            _owner.LogListViewState("SEARCH", "begin-empty-before-reset");
            _owner.ResetListViewportTopAsync(0, "SEARCH-empty");
            SetSearchStatus(Localization.T("status_searching_progress"), 0, 0);
            _owner.RefreshSearchOverlayVisibility();
            if (_owner._listView.Columns.Count == 0 ||
                (_owner._listView.Columns[0].Tag as ColumnMeta)?.Key != "col_name")
            {
                _owner.SetupFileColumns(_owner._listView);
            }

            List<FileItem> results = new List<FileItem>();
            int finalScanned = 0;
            int lastReportedScanned = -1;
            long lastReportTick = Environment.TickCount64;
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

                bool firstBatchViewportReset = false;

                var uiUpdateAction = new Action<List<FileItem>>(foundBatch =>
                {
                    if (foundBatch == null || foundBatch.Count == 0 || cts.Token.IsCancellationRequested) return;

                    _owner.Invoke(new Action(() =>
                    {
                        if (IsCurrentSearch(cts))
                        {
                            results.AddRange(foundBatch);
                            _owner._items = results;
                            RefreshVirtualListSize();
                            if (!firstBatchViewportReset && results.Count > 0)
                            {
                                firstBatchViewportReset = true;
                                _owner.LogListViewState("SEARCH", "first-batch-before-reset");
                                _owner.ResetListViewportTopAsync(0, "SEARCH-first-batch");
                            }
                            _owner._listView.Invalidate();
                            _owner.RefreshSearchOverlayVisibility();
                        }
                    }));
                });

                if (IsTagSearchOnly)
                {
                    SetSearchStatus(Localization.T("status_searching_tags"));
                    await FileSystemService.SearchTagsAsync(
                        _owner._currentPath,
                        query,
                        uiUpdateAction,
                        cts.Token);
                    finalScanned = results.Count;
                }
                else if (_owner._currentPath == ThisPcPath)
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
                        catch { }
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
                        _owner._currentPath,
                        query,
                        progress,
                        uiUpdateAction,
                        cts.Token);
                    finalScanned = pathSearched;
                }

                if (!IsCurrentSearch(cts)) return;

                FileSystemService.SortItems(results, _owner._sortColumn, _owner._sortDirection);
                _owner._items = results;
                IsSearchInProgress = false;

                _owner._listView.BeginUpdate();
                try
                {
                    _owner._listView.SelectedIndices.Clear();
                    _owner._listView.VirtualListSize = 0;
                    _owner._listView.VirtualListSize = _owner._items.Count;
                    if (_owner._items.Count > 0)
                    {
                        try { _owner._listView.TopItem = _owner._listView.Items[0]; } catch { }
                        try { _owner._listView.Items[0].EnsureVisible(); } catch { }
                    }
                }
                finally
                {
                    _owner._listView.EndUpdate();
                }

                StopStatusSpinner();
                finalScanned = Math.Max(finalScanned, _owner._items.Count);
                _owner._statusLabel.Text = string.Format(Localization.T("status_search_done"), _owner._items.Count, finalScanned);
                _owner.LogListViewState("SEARCH", "done-before-reset");
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
                            FileSystemService.SortItems(results, _owner._sortColumn, _owner._sortDirection);
                            _owner._items = results;
                            IsSearchInProgress = false;
                            _owner._listView.VirtualListSize = _owner._items.Count;
                            StopStatusSpinner();
                            _owner._statusLabel.Text = string.Format(Localization.T("status_search_stopped"), _owner._items.Count);
                            _owner.LogListViewState("SEARCH", "stopped-before-reset");
                            _owner.ResetListViewportTopAsync(0, "SEARCH-stopped");
                            _owner._listView.Invalidate();
                            _owner.RefreshSearchOverlayVisibility();
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                if (!IsCurrentSearch(cts)) return;
                StopStatusSpinner();
                _owner._statusLabel.Text = string.Format(Localization.T("status_error"), ex.Message);
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
            _owner._statusLabel.Text = $"{_searchStatusBase} {_spinnerFrames[_spinnerFrameIndex]}";
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
                    _owner._statusLabel.Text = $"{_searchStatusBase} {_spinnerFrames[_spinnerFrameIndex]}";
                    if (HasProgressRow)
                        _owner.InvalidateListItem(_owner._items.Count);
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
            if (_owner._listView == null || _owner._listView.IsDisposed || !_owner._listView.VirtualMode)
                return;

            int target = _owner._items.Count + (HasProgressRow ? 1 : 0);
            if (_owner._listView.VirtualListSize != target)
                _owner._listView.VirtualListSize = target;
        }
    }
}
